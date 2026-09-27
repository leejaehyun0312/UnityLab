using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(SurfaceContactAgent))]
public class SnowInteractor : MonoBehaviour
{
    static readonly int SnowInteractorPositionId = Shader.PropertyToID("_SnowInteractorPosition");

    [SerializeField] SurfaceContactAgent contactAgent;
    [SerializeField] SnowStateStorage storage;
    [FormerlySerializedAs("footprintProfile")]
    [SerializeField] SnowStampProfile trailProfile;
    [SerializeField] bool drivesShaderFocus = true;

    [Header("Trail")]
    [SerializeField, Min(0.005f)] float brushSpacing = 0.04f;
    [SerializeField, Min(0.1f)] float teleportDistance = 1.5f;
    [SerializeField, Range(0f, 1f)] float pressure = 1f;
    [SerializeField] int trailPriority = 100;

    SnowSurface currentSurface;
    Vector3 lastPosition;
    bool hasContact;

    void Reset() => contactAgent = GetComponent<SurfaceContactAgent>();

    void Awake()
    {
        if (!contactAgent) contactAgent = GetComponent<SurfaceContactAgent>();
        if (contactAgent) contactAgent.SetAlwaysActive(drivesShaderFocus);
    }

    public void Configure(SurfaceContactAgent agent, SnowStateStorage stateStorage, SnowStampProfile profile, bool shaderFocus = false, int priority = 0)
    {
        contactAgent = agent;
        storage = stateStorage;
        trailProfile = profile;
        drivesShaderFocus = shaderFocus;
        trailPriority = priority;
        if (contactAgent) contactAgent.SetAlwaysActive(shaderFocus);
    }

    void LateUpdate()
    {
        if (drivesShaderFocus)
        {
            Shader.SetGlobalVector(SnowInteractorPositionId, transform.position);
            SurfaceContactSystem.SetFocus(transform);
        }

        if (!contactAgent || !storage || !trailProfile || !contactAgent.HasContact || !contactAgent.CanInteract || contactAgent.Contact.Surface is not SnowSurface surface)
        {
            EndTrail();
            return;
        }

        SurfaceContact contact = contactAgent.Contact;
        Vector3 position = contact.Point;
        if (!hasContact || currentSurface != surface)
        {
            BeginTrail(surface, position, contact.Normal);
            return;
        }

        Vector3 movement = Vector3.ProjectOnPlane(position - lastPosition, contact.Normal);
        float distance = movement.magnitude;
        if (distance < brushSpacing) return;

        if (distance > teleportDistance)
        {
            BeginTrail(surface, position, contact.Normal);
            return;
        }

        Vector3 direction = movement.sqrMagnitude > 0.0001f ? movement.normalized : GetForward(contact.Normal);
        storage.ApplyBrush(surface, new SurfaceBrush(lastPosition, position, direction, pressure, trailProfile, trailPriority));
        lastPosition = position;
    }

    void BeginTrail(SnowSurface surface, Vector3 position, Vector3 normal)
    {
        storage.ApplyBrush(surface, new SurfaceBrush(position, GetForward(normal), pressure, trailProfile, trailPriority));
        currentSurface = surface;
        lastPosition = position;
        hasContact = true;
    }

    void EndTrail()
    {
        currentSurface = null;
        hasContact = false;
    }

    Vector3 GetForward(Vector3 normal)
    {
        Vector3 direction = Vector3.ProjectOnPlane(transform.forward, normal);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
