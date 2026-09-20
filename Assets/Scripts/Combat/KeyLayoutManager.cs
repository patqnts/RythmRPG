using UnityEngine;

public class KeyLayoutManager : MonoBehaviour
{
    public GameObject[] Keys;
    public Transform faceTarget;
    public float curveIntensity = 0.75f;
    [Min(0f)] public float horizontalSpacing = 1.25f;
    public float verticalPosition = -3.5f;
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
        if (Keys == null) return;
        float center = (Keys.Length - 1) * 0.5f;
        for (int index = 0; index < Keys.Length; index++)
        {
            if (Keys[index] == null) continue;
            Keys[index].transform.localPosition = new Vector2((index - center) * horizontalSpacing, verticalPosition);
        }
    }


    public void SmileLayout()
    {
        if (Keys == null) return;
        float width = Mathf.Max(0.01f, horizontalSpacing);
        float center = (Keys.Length - 1) * 0.5f;

        for (int i = 0; i < Keys.Length; i++)
        {
            if (Keys[i] == null) continue;
            float x = (i - center) * width;
            float y = verticalPosition + Mathf.Pow(x / width, 2) * curveIntensity;
            Keys[i].transform.localPosition = new Vector2(x, y);

            // Rotate to face the target on the 2D plane
            if (faceTarget != null)
            {
                Vector3 direction = faceTarget.position - Keys[i].transform.position;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90;
                Keys[i].transform.rotation = Quaternion.Euler(0, 0, angle);
            }
        }
    }

}
