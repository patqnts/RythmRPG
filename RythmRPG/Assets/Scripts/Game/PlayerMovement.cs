using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    
    public Rigidbody2D body;
    public bool isEnabled;
    public CircleCollider2D circleCollider;
    public GameObject noticeObject;
    public GameObject battleBg;

    Vector2 direction;
    public SpriteRenderer spriteRenderer;
    public List<Sprite> nSprites;
    public List<Sprite> neSprites;
    public List<Sprite> eSprites;
    public List<Sprite> seSprites;
    public List<Sprite> sSprites;
    public float walkSpeed;
    public float frameRate;
    float idleTime;
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

            //float inputX = Input.GetAxis("Horizontal");
            //float inputY = Input.GetAxis("Vertical");

            //if(Mathf.Abs(inputX) > 0)
            //{
            //    rigidbody.velocity = new Vector2 (inputX*speed, rigidbody.velocity.y);
            //}
            //if (Mathf.Abs(inputY) > 0)
            //{
            //    rigidbody.velocity = new Vector2(rigidbody.velocity.x, inputY*speed);
            //}

            //if(inputX < 0)
            //{
            //    gameObject.GetComponent<SpriteRenderer>().flipX = true;
            //}
            //else
            //{
            //    gameObject.GetComponent<SpriteRenderer>().flipX = false;
            //}
            //get direction of input

            direction = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).normalized;
            //set walk based on direction
            body.velocity = direction * walkSpeed;
            HandleSpriteFlip();
            SetSprite();

        }

       
    }

    void SetSprite()
    {
        List<Sprite> directionSprites = GetSpriteDirection();
        if (directionSprites != null)
        {
            //holding a direction.
            float playTime = Time.time - idleTime; //time since we started walking
            int totalFrames = (int)(playTime * frameRate); //total frames since we started
            int frame = totalFrames % directionSprites.Count; //current frame
            spriteRenderer.sprite = directionSprites[frame];
        }
        else
        {
            //holding nothing, input is neutral
            idleTime = Time.time;
        }
    }
    void HandleSpriteFlip()
    {
        //if we're facing right, and the player holds left, flip.
        if (!spriteRenderer.flipX && direction.x < 0)
        {
            spriteRenderer.flipX = true;
            //if we're facing left and the player hold right, flip.
        }
        else if (spriteRenderer.flipX && direction.x > 0)
        {
            spriteRenderer.flipX = false;
        }
    }
    List<Sprite> GetSpriteDirection()
    {
        List<Sprite> selectedSprites = null;
        if (direction.y > 0)
        {//north
            if (Mathf.Abs(direction.x) > 0)
            {//east or west
                selectedSprites = neSprites;
            }
            else
            {//neutral x
                selectedSprites = nSprites;
            }
        }
        else if (direction.y < 0)
        {//south
            if (Mathf.Abs(direction.x) > 0)
            {//east or west
                selectedSprites = seSprites;
            }
            else
            {//neutral x
                selectedSprites = sSprites;
            }
        }
        else
        {//neutral
            if (Mathf.Abs(direction.x) > 0)
            {//east or west
                selectedSprites = eSprites;
            }
        }
        return selectedSprites;
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
        SoundHandler.Instance.PlayEncounterSound();
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
