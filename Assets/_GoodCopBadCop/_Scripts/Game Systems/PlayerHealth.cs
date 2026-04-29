using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public UnityAction OnHealthChanged;
    
    public void TakeDamage(float damage)
    {
        Health -= damage;
        OnHealthChanged?.Invoke();
    }
    
    public void Heal(float healAmount)
    {
        Health += healAmount;
        OnHealthChanged?.Invoke();
    }
}
