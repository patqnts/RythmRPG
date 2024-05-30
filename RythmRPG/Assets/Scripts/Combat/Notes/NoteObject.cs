    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Principal;
    using UnityEngine;

    public class NoteObject : Note, INote
    {
        [SerializeField]
        private int noteIdentity;
    
        [SerializeField] public float speed;
        [SerializeField] public int damage;

        [SerializeField] public KeyButton[] keys;

        public bool canBePressed;

        public KeyCode keyCode;
        public Moveset moveset;
        bool INote.canBePressed { get => this.canBePressed;}
        public bool isMoving;
        public bool isSpecial;
        public Animator animator;
        private void Start()
        {
            CombatManager.instance.StopAttackEvent += DestroyObject;
            keys = FindObjectsOfType<KeyButton>();   
            stateHandler = FindObjectOfType<PlayerStateHandler>();
            //animator.SetBool(moveset.ToString(), true);      
            isMoving = true;
        }

        public virtual void Update()
        {  
            KeyButton identityButton = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault();
            if(Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
            {
                if(canBePressed)
                {
                    CombatManager.instance.DamageOpponent(damage);
                    DestroyObject();
                }
            }
            
            if (isMoving)
            {
                keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(noteIdentity);
                float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
                float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

                // Smoothly interpolate between current position and target position
                transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

                transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);

                //Destroy(gameObject, 5f);
            }
        
        }

        public void SetNoteIdentity(int i)
        {
            noteIdentity = i;
        }

        public int GetNoteIdentity()
        {
            return noteIdentity;
        }

        public void DestroyObject()
        {
            isMoving = false;
            animator.SetTrigger("Hit");
            Destroy(gameObject, .25f);
        }

        private void OnDestroy()
        {
            CombatManager.instance.StopAttackEvent -= DestroyObject;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if(other.gameObject.tag == "Activator")
            {
                canBePressed = true;            
            }
        }
        private void OnTriggerExit2D(Collider2D other)
        {
            if(other.gameObject.tag == "Activator" && isMoving)
            {
                canBePressed = false;
                SetPlayerState(PlayerState.Freeze, 3f);
                PlayerData.instance.TakeDamage(damage);
                DestroyObject();
            }
        }
    }
