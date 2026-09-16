using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WaterTrigger : MonoBehaviour
{
    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        WaterInteractor interactor =
            other.GetComponentInParent<WaterInteractor>();

        if (interactor != null)
        {
            Debug.Log("ENTER WATER");
            interactor.EnterWater();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        WaterInteractor interactor =
            other.GetComponentInParent<WaterInteractor>();

        if (interactor != null)
        {
            interactor.ExitWater();
        }
    }
}