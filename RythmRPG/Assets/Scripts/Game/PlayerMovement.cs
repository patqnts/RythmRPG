using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed;
    public Rigidbody2D rigidbody;
    public bool isEnabled;
    public CircleCollider2D circleCollider;
    private void Start()
    {
        CombatManager.instance.ExitCombatEvent += EnableMovement;
    }

    private void OnDisable()
    {
        CombatManager.instance.ExitCombatEvent -= EnableMovement;
     
    }
    // Update is called once per frame
    void Update()
    {
        circleCollider.enabled = Input.GetKey(KeyCode.Return);
        if (isEnabled) 
        {
            

            float inputX = Input.GetAxis("Horizontal");
            float inputY = Input.GetAxis("Vertical");

            if(Mathf.Abs(inputX) > 0)
            {
                rigidbody.velocity = new Vector2 (inputX*speed, rigidbody.velocity.y);
            }
            if (Mathf.Abs(inputY) > 0)
            {
                rigidbody.velocity = new Vector2(rigidbody.velocity.x, inputY*speed);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        IEnemy enemy = collision.gameObject.GetComponent<IEnemy>();
        GameObject enemyObject = collision.gameObject;
        
        if (enemy != null)
        {
           CombatManager.instance.InitalizeCombat(this.gameObject, enemyObject);
        }
    }


    public void EnableMovement()
    {
        isEnabled = true;
    }

    public void DisableMovement()
    {
        isEnabled = false;
    }
}
