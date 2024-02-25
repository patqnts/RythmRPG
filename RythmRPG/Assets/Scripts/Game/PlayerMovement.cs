using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed;
    public Rigidbody2D rigidbody;

    // Update is called once per frame
    void Update()
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

    private void OnTriggerEnter2D(Collider2D collision)
    {
        IEnemy enemy = collision.gameObject.GetComponent<IEnemy>();
        if(enemy != null)
        {
            Debug.Log("Enemy Encountered: " + collision.gameObject.name);
        }
    }
}
