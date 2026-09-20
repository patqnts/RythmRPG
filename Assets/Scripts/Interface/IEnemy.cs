using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEnemy 
{
    public int MaxHealth { get; set; }
    public int CurrentHealth { get; set; }

    public void TakeDamage(int damage);
    public void AttackAnimate();
    public void DeathAnimate();
    public void PoseAnimate();
    public void IdleAnimate();
}
