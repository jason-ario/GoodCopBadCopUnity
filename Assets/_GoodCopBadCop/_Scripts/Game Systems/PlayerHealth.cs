using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100;
    public float MaxHealth => maxHealth;
    public UnityAction OnHealthChanged;
    public UnityAction OnDeath;

    private float _health= 100;
    private bool _isDead;

    public bool IsDead => _isDead;

    public float Health
    {
        get => _health;
        set => _health = Mathf.Clamp(value, 0f, MaxHealth);
    }

    /// <summary>Reduces health by the given amount and fires OnDeath when health reaches zero.</summary>
    public void TakeDamage(float damage)
    {
        if (_isDead) return;

        Health -= damage;
        OnHealthChanged?.Invoke();

        if (Health <= 0f)
        {
            _isDead = true;
            OnDeath?.Invoke();
        }
    }

    /// <summary>Restores health by the given amount. Has no effect while the player is dead.</summary>
    public void Heal(float healAmount)
    {
        if (_isDead) return;

        Health += healAmount;
        OnHealthChanged?.Invoke();
    }

    /// <summary>Resets health to max and clears the dead state.</summary>
    public void ResetHealth()
    {
        _isDead = false;
        Health = MaxHealth;
        OnHealthChanged?.Invoke();
    }
}
