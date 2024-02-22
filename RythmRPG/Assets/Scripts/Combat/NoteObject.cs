using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NoteObject : MonoBehaviour
{
    [SerializeField]
    public int noteIdentity;

    [SerializeField] public float speed;

    [SerializeField] KeyButton[] keys;
    private void Start()
    {
        keys = FindObjectsOfType<KeyButton>();
        speed /= 60f;
    }

    private void Update()
    {
        // Example input, you can modify this based on your input method
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            noteIdentity = 1;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            noteIdentity = 2;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            noteIdentity = 3;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            noteIdentity = 4;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            noteIdentity = 5;
        }

        float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
        float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

        // Smoothly interpolate between current position and target position
        transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

        transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);

        Destroy(gameObject,5f);
    }
}
