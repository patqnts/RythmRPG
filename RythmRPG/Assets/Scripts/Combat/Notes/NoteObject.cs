    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Principal;
    using UnityEngine;

    public class NoteObject : Note, INote
    {
        [SerializeField] private int noteIdentity;   
        [SerializeField] public float speed;
        [SerializeField] public int damage;
        [SerializeField] public KeyButton[] keys;

        public Animator animator;
        public bool canBePressed;

        public KeyCode keyCode;
        public Moveset moveset;
        public PlayerState state;
        public HitEffect hitEffect;

        bool INote.canBePressed { get => this.canBePressed;}
        public bool isMoving;
        public bool isSpecial;
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
                    
                //DestroyObject(); //DEFAULT EFFECT
                    StartHitEffect();
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
            }
        
        }
        
    public void StartHitEffect()
    {
        switch (hitEffect)
        {
            case HitEffect.Default:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Ghost:
                PlayerData.instance.TakeDamage(damage);
                DestroyObject();                
                break;
            case HitEffect.DoubleHit:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Cluster:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Pong:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;

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


        public void SetSpeed(float i)
        {
            speed = i;
        }

        public float GetSpeed()
        {
            return speed;
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
                SetPlayerState(state, 30);
                PlayerData.instance.TakeDamage(damage);
                DestroyObject();
            }
        }
    }
