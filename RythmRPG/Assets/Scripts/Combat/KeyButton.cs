using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyButton : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] public int keyIdentity;
    public bool isPressed;
    public Sprite[] sprites;
    public SpriteRenderer spriteRenderer;

    private void Update()
    {
        // Set the sprite based on the isPressed value
        if (isPressed)
        {
            spriteRenderer.sprite = sprites[0]; // Assuming 1 is the index for the pressed state in your sprites array
        }
        else
        {
            spriteRenderer.sprite = sprites[1]; // Assuming 0 is the index for the not pressed state in your sprites array
        }
    }
}
