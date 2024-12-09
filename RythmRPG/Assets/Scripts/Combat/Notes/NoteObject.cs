using DG.Tweening;
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
            if(canBePressed)
            {
                if(Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
                {

                    //DestroyObject(); //DEFAULT EFFECT
                    //FindObjectOfType<HitStop>().Stop(this.gameObject, 0.02F);
                    FindObjectOfType<ScreenshakeManager>().ShakeLight();
                    StartHitEffect(1,identityButton.keyType);
                }
            }
            
            if (isMoving)
            {
                keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
                float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
                float targetY = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.y -3;
                float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

            // Smoothly interpolate between current position and target position
            //transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

            //transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);
                transform.DOMoveX(targetX, 0.25F).SetEase(Ease.OutSine);
                transform.DOMoveY(targetY, 2.5f).SetEase(Ease.Linear);
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
                transform.DOKill();
                PlayerData.instance.TakeDamage(damage);
                DestroyObject();
            }
        }
    }
