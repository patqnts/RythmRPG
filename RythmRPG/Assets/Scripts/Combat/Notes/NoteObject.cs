    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Principal;
    using UnityEngine;

    public class NoteObject : MonoBehaviour, INote
    {
        [SerializeField]
        public int noteIdentity;
    
        [SerializeField] public float speed;

        [SerializeField] public KeyButton[] keys;

        public bool canBePressed;

        public KeyCode keyCode;

        bool INote.canBePressed { get => this.canBePressed;}
        public bool isMoving;
        public bool isSpecial;
        public Animator animator;
        private void Start()
        {      
            keys = FindObjectsOfType<KeyButton>();   
            animator.SetBool("Special", isSpecial);           
            isMoving = true;
        }

        public virtual void Update()
        {  
            if(Input.GetKeyDown(keyCode))
            {
                if(canBePressed)
                {
                    isMoving = false;
                    animator.SetTrigger("Hit");
                    Destroy(gameObject,.25f);
                    Debug.Log("Hit");
                }
            }

            if (isMoving)
            {
                float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
                float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

                // Smoothly interpolate between current position and target position
                transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

                transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);

                Destroy(gameObject, 5f);
            }
        
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
            if(other.gameObject.tag == "Activator")
            {
                canBePressed = false;
                Debug.Log("Miss");
            }
        }
    }
