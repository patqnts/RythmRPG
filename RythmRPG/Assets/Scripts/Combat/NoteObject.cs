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

    [SerializeField] KeyButton[] keys;

    public bool canBePressed;

    public KeyCode keyCode;

    bool INote.canBePressed { get => this.canBePressed;}

    private void Start()
    {      
        keys = FindObjectsOfType<KeyButton>();
        speed /= 60f;
    }

    private void Update()
    {  
        if(Input.GetKeyDown(keyCode) )
        {
            if(canBePressed)
            {
                Destroy(gameObject);
            }
        }
        
        float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
        float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

        // Smoothly interpolate between current position and target position
        transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

        transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);

        Destroy(gameObject,5f);
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
        }
    }

    public KeyCode GetKeyCodeFromNoteIdentity(int identity)
    {
        // Assuming noteIdentity is in the range of 1 to 5
        switch (identity)
        {
            case 1:
                return KeyCode.Alpha1;
            case 2:
                return KeyCode.Alpha2;
            case 3:
                return KeyCode.Alpha3;
            case 4:
                return KeyCode.Alpha4;
            case 5:
                return KeyCode.Alpha5;
            default:
                return KeyCode.None;
        }
    }


}
