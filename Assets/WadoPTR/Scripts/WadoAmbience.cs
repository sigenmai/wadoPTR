using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>蛍光灯のちらつきと、どこかで落ちる水滴。</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoAmbience : UdonSharpBehaviour
{
    public Light flickerLight;
    public Renderer flickerTube;
    public AudioSource dripSource;
    public AudioClip clipDrip;
    public Transform[] dripPoints;

    private float baseIntensity;
    private float flickerT;
    private float nextFlicker;
    private float nextDrip;
    private MaterialPropertyBlock block;
    private Color tubeColor;

    void Start()
    {
        if (Utilities.IsValid(flickerLight)) baseIntensity = flickerLight.intensity;
        block = new MaterialPropertyBlock();
        tubeColor = new Color(0.85f, 0.95f, 1f) * 2.2f;
        nextFlicker = Random.Range(6f, 20f);
        nextDrip = Random.Range(3f, 9f);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        nextFlicker -= dt;
        if (nextFlicker <= 0f)
        {
            nextFlicker = Random.Range(8f, 35f);
            flickerT = Random.Range(0.25f, 0.9f);
        }
        if (flickerT > 0f)
        {
            flickerT -= dt;
            float k = flickerT <= 0f ? 1f : (Random.value > 0.45f ? 1f : Random.Range(0.05f, 0.4f));
            if (Utilities.IsValid(flickerLight)) flickerLight.intensity = baseIntensity * k;
            if (Utilities.IsValid(flickerTube))
            {
                block.SetColor("_EmissionColor", tubeColor * k);
                flickerTube.SetPropertyBlock(block);
            }
        }

        nextDrip -= dt;
        if (nextDrip <= 0f)
        {
            nextDrip = Random.Range(2.5f, 11f);
            if (Utilities.IsValid(dripSource) && Utilities.IsValid(clipDrip))
            {
                if (Utilities.IsValid(dripPoints) && dripPoints.Length > 0)
                {
                    dripSource.transform.position = dripPoints[Random.Range(0, dripPoints.Length)].position;
                }
                dripSource.pitch = Random.Range(0.85f, 1.2f);
                dripSource.PlayOneShot(clipDrip, Random.Range(0.25f, 0.6f));
            }
        }
    }
}
