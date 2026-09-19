using System.Collections;
using TalesTensor.Map; // ProceduralIcons
using UnityEngine;

/// <summary>
/// A code-built swirling particle "portal" the AR character emerges from when it's
/// placed, so it doesn't just pop into existence. A vertical ring of glowing particles
/// faces the camera; call <see cref="FadeOutAndDestroy"/> once the character has stepped
/// through and it stops emitting, lets the last particles die, then cleans itself up.
///
/// Procedural so it ships with no VFX asset — swap in an authored ParticleSystem/VFX
/// prefab later for something fancier.
/// </summary>
[DisallowMultipleComponent]
public class ARPortal : MonoBehaviour
{
    ParticleSystem _ps;

    /// <summary>Create a portal centred at <paramref name="center"/>, its ring facing along
    /// <paramref name="facing"/> (forward = toward the viewer), with the given ring radius.</summary>
    public static ARPortal Create(Vector3 center, Quaternion facing, float radius)
    {
        var go = new GameObject("ARPortal");
        go.transform.SetPositionAndRotation(center, facing);
        var portal = go.AddComponent<ARPortal>();
        portal.Build(radius);
        return portal;
    }

    void Build(float radius)
    {
        _ps = gameObject.AddComponent<ParticleSystem>();
        _ps.Stop(); // configure before it plays

        var main = _ps.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = new Color(0.45f, 0.78f, 1f, 1f); // arcane cyan-blue
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 800;

        var emission = _ps.emission;
        emission.rateOverTime = 320f;

        // Emit from the rim of a circle (a ring/doorway), in the transform's local XY plane.
        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0.22f;
        shape.arc = 360f;

        // Swirl the ring around its facing axis for a vortex feel.
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(1.6f);

        // Fade particles in then out over their life.
        var colOverLife = _ps.colorOverLifetime;
        colOverLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.6f, 0.9f, 1f), 0f),
                new GradientColorKey(new Color(0.25f, 0.55f, 1f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f),
            });
        colOverLife.color = grad;

        var renderer = _ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.material = PortalMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        _ps.Play();
    }

    static Material PortalMaterial()
    {
        // Prefer the URP particle shader; fall back to the URP sprite-friendly default.
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);

        var tex = ProceduralIcons.Circle().texture; // soft round dot
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        // Additive, transparent — glowy particles.
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);     // additive
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    /// <summary>After <paramref name="afterSeconds"/>, stop emitting and let the remaining
    /// particles fade out, then destroy the portal.</summary>
    public void FadeOutAndDestroy(float afterSeconds, float tailSeconds = 1.6f)
    {
        StartCoroutine(FadeRoutine(afterSeconds, tailSeconds));
    }

    IEnumerator FadeRoutine(float delay, float tail)
    {
        yield return new WaitForSeconds(delay);
        if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        yield return new WaitForSeconds(tail);
        Destroy(gameObject);
    }
}
