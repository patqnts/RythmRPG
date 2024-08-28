using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyLayoutManager : MonoBehaviour
{
    public GameObject[] Keys;
    public Transform faceTarget;
    public float curveIntensity = 0.75f;
    void Start()
    {
         DefaultLayout();
         //SmileLayout();
    }

    // Update is called once per frame
    void Update()
    {
        //SmileLayout();
    }
    public void DefaultLayout()
    {
        Keys[0].gameObject.transform.localPosition = new Vector2(-2.5f, -3.5f);
        Keys[1].gameObject.transform.localPosition = new Vector2(-1.25f, -3.5f);
        Keys[2].gameObject.transform.localPosition = new Vector2(0, -3.5f);
        Keys[3].gameObject.transform.localPosition = new Vector2(1.25f, -3.5f);
        Keys[4].gameObject.transform.localPosition = new Vector2(2.5f, -3.5f);
    }


    public void SmileLayout()
    {
        float width = 1.5f; // Horizontal spacing factor

        for (int i = 0; i < Keys.Length; i++)
        {
            float x = -2 * width + i * width;
            float y = -3.5f + Mathf.Pow(x / width, 2) * curveIntensity;
            Keys[i].transform.localPosition = new Vector2(x, y);

            // Rotate to face the target on the 2D plane
            Vector3 direction = faceTarget.position - Keys[i].transform.position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90; // Adjust for correct rotation
            Keys[i].transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

}
