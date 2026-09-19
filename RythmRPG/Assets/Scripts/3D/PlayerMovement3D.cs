using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement3D : MonoBehaviour
{
    [SerializeField] private float speed = 4f;
    [SerializeField] private bool movementEnabled = true;

    private CharacterController controller;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!movementEnabled)
        {
            controller.SimpleMove(Vector3.zero);
            return;
        }

        Vector2 input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;

        Vector3 movement = new Vector3(input.x, 0f, input.y);

        controller.SimpleMove(movement * speed);
    }

    public void DisableMovement()
    {
        movementEnabled = false;
        controller.SimpleMove(Vector3.zero);
    }

    public void EnableMovement()
    {
        movementEnabled = true;
    }
}
