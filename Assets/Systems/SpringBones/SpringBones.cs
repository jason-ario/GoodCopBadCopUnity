
using System.Collections.Generic;
using UnityEngine;

namespace SpringBonesTool
{
    public class SpringBones : MonoBehaviour
    {
        private enum Axis { X, Y, Z }

        //rotation from ZY to
        private static readonly Quaternion[] QRots = new Quaternion[3]
        {
        new Quaternion(0, -.7071f, 0, .7071f), //XY
        new Quaternion(0,.7071f,.7071f,0), //YZ
        new Quaternion(0,0,.7071f,.7071f) //ZX
        };

        private class Item
        {
            private readonly Transform bone;
            private readonly Transform parent;
            private readonly int axis;

            private readonly Vector3 pLookDir; //look dir in parent space
            private readonly Vector3 pSecDir; //second dir in parent space
            private Vector3 wBasePos; //base pos in world space
            private Vector3 wMassPos; //dynamic mass pos in world space

            private Vector3 delta;
            private Vector3 speed;

            private Vector3 aMain;
            private Vector3 aSec;
            private Quaternion qrot;

            public int deep;

            public Item(Transform bone, int axis, float len)
            {
                this.bone = bone;
                parent = bone.parent;

                this.axis = axis;

                Vector3 lookDir = Vector3.forward;
                Vector3 upDir = Vector3.up;

                switch (axis)
                {
                    case 0:
                        lookDir = bone.right;
                        upDir = bone.up;
                        break;

                    case 1:
                        lookDir = bone.up;
                        upDir = bone.forward;
                        break;

                    case 2:
                        lookDir = bone.forward;
                        upDir = bone.right;
                        break;
                }

                wBasePos = bone.position + lookDir * len;
                wMassPos = wBasePos;

                if (parent != null)
                {
                    pLookDir = parent.InverseTransformDirection(lookDir);
                    pSecDir = parent.InverseTransformDirection(upDir);
                }
                else
                {
                    pLookDir = lookDir;
                    pSecDir = upDir;
                }

                //define deep
                deep = 0;
                var t = bone;
                while (t.parent != null)
                {
                    t = t.parent;
                    deep++;
                }
            }

            public void Tick(float rigid, float damping, float length, float impact)
            {
                //calc base point to world
                wBasePos = bone.position + (parent != null ? parent.TransformDirection(pLookDir) : pLookDir) * length;

                //clamp maximum delta by 45 grad
                delta = wBasePos - wMassPos;
                if (delta.sqrMagnitude > length * length)
                {
                    delta = delta.normalized * length;
                    if (length < 0)
                        delta *= -1f;

                    wMassPos = wBasePos - delta;
                }
                
                //calc dynamic
                speed += rigid * Time.deltaTime * delta;
                speed *= damping;
                wMassPos += speed * Time.deltaTime;

                //calc orientation look to mass
                aMain = (wMassPos - bone.position).normalized;
                if (length < 0)
                    aMain *= -1f;

                aSec = (parent == null) ? pSecDir : parent.TransformDirection(pSecDir);
                qrot = Quaternion.LookRotation(aMain, aSec);

                bone.rotation = Quaternion.Lerp(bone.rotation, qrot * QRots[axis], impact);
            }
        }

        [SerializeField] private float length = 1f;
        [SerializeField] private float rigid = 100f;
        [SerializeField] [Range(0, 1f)] private float damping = .95f;
        [SerializeField] [Range(0, 1f)] private float impact = 1f;

        [SerializeField] private Axis axis;
        private int iaxis;

        [SerializeField] private List<Transform> bones;
        private List<Item> items;

        public bool doUpdate = true;

        private void Awake()
        {
            Init();
        }

        private void Init()
        {
            if (items == null)
                items = new List<Item>();

            items.Clear();

            iaxis = (int)axis;

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] == null)
                    continue;

                items.Add(new Item(bones[i], iaxis, length));
            }

            //sort by hierarchy
            items.Sort((x, y) => x.deep.CompareTo(y.deep));
        }

        private void LateUpdate()
        {
            for (int i = 0; i < items.Count; i++)
                items[i].Tick(rigid, 1f - damping, length, impact);
        }

        private void OnDrawGizmosSelected()
        {
            if (bones == null)
                return;

            Vector3 end;
            iaxis = (int)axis;

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i] == null)
                    continue;
            
                end = bones[i].position +
                    (iaxis == 0 ? bones[i].right :
                     iaxis == 1 ? bones[i].up :
                     bones[i].forward) * length;
            
                Gizmos.DrawLine(bones[i].position, end);
                Gizmos.DrawWireSphere(end, Mathf.Abs(length) / 10f);
            }
        }
    }
}
