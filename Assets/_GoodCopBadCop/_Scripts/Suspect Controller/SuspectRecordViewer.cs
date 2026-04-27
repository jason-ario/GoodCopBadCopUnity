using Unity.Collections;
using UnityEngine;

public class SuspectRecordViewer : MonoBehaviour
{
    [SerializeField, ReadOnly] 
    private SuspectRecord record;

    public void SetRecord(SuspectRecord runtimeRecord)
    {
        record = runtimeRecord;
    }
}