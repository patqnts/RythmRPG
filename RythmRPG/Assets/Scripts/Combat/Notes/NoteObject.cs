    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Principal;
    using UnityEngine;

    public class NoteObject : Note, INote
    {        
        bool INote.canBePressed { get => this.canBePressed;}
        
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
            KeyButton identityButton = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault();
            if(Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
            {
                if(canBePressed)
                {
                    
                //DestroyObject(); //DEFAULT EFFECT
                    StartHitEffect(1);
                }
            }
            
            if (isMoving)
            {
                keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
                float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
                float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

                // Smoothly interpolate between current position and target position
                transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

                transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);
            }
        
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
