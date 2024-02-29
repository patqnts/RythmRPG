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
    public GameObject noticeObject;
    public GameObject battleBg;
    private void Start()
    {
        CombatManager.instance.ExitCombatEvent += EnableMovement;
        CombatManager.instance.ExitCombatEvent += CloseBattleBG;
    }

    private void OnDisable()
    {
        CombatManager.instance.ExitCombatEvent -= EnableMovement;
        CombatManager.instance.ExitCombatEvent -= CloseBattleBG;
     
    }
    // Update is called once per frame
    void Update()
    {
        
        if (isEnabled) 
        {
            circleCollider.enabled = Input.GetKey(KeyCode.Return);

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
            StartCoroutine(EncounterTransition(enemyObject));
        }
    }

    IEnumerator EncounterTransition(GameObject enemyObject)
    {
        battleBg.SetActive(true);

        if (enemyObject != null)
        {
            enemyObject.GetComponent<SpriteRenderer>().sortingOrder = CombatManager.instance.charactersSortOrder;            
        }

        isEnabled = false;
        circleCollider.enabled = false;
        GameObject notice = Instantiate(noticeObject, enemyObject.transform);
        yield return new WaitForSeconds(1.5f);
        Destroy(notice);
        CombatManager.instance.InitalizeCombatEvent(this.gameObject, enemyObject);
    }

    public void CloseBattleBG()
    {
        battleBg.SetActive(false);
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
