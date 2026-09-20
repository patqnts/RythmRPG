using UnityEngine;

public class WaterRippleManager : MonoBehaviour
{
    public static WaterRippleManager Instance { get; private set; }

    private const int MaxRipples = 32;
    private readonly Vector4[] ripples = new Vector4[MaxRipples];

    private int nextRippleIndex;
    private int rippleCount;

    private static readonly int RipplesID = Shader.PropertyToID("_WaterRipples");
    private static readonly int RippleCountID = Shader.PropertyToID("_WaterRippleCount");

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < MaxRipples; i++)
            ripples[i] = new Vector4(0, 0, -9999, 0);
    }

    private void Start()
    {
        Upload();
    }

    public void AddRipple(Vector3 worldPosition, float strength = 1f)
    {
        ripples[nextRippleIndex] = new Vector4(
            worldPosition.x,
            worldPosition.z,
            Time.time,
            strength
        );

        nextRippleIndex = (nextRippleIndex + 1) % MaxRipples;
        rippleCount = Mathf.Min(rippleCount + 1, MaxRipples);

        Upload();
    }

    private void Upload()
    {
        Shader.SetGlobalVectorArray(RipplesID, ripples);
        Shader.SetGlobalInt(RippleCountID, rippleCount);
    }
}
