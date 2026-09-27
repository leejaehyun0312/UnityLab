using UnityEngine;
using UnityEngine.Serialization;

public readonly struct SurfaceContact
{
    public Component Surface { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public float Depth { get; }

    public SurfaceContact(Component surface, Vector3 point, Vector3 normal, float depth)
    {
        Surface = surface;
        Point = point;
        Normal = normal;
        Depth = depth;
    }
}

public interface IContactSurface
{
    Component Component { get; }
    Bounds ContactBounds { get; }
    int Priority { get; }
    bool TrySample(Vector3 position, out SurfaceContact contact);
}

[DisallowMultipleComponent]
public class SurfaceContactAgent : MonoBehaviour
{
    [FormerlySerializedAs("bodyCollider")]
    [SerializeField] Collider contactCollider;
    [SerializeField, Min(0f)] float contactTolerance = 0.05f;
    [SerializeField] bool alwaysActive = true;

    public bool HasContact { get; private set; }
    public SurfaceContact Contact { get; private set; }
    public Collider ContactCollider => contactCollider;
    public float ContactTolerance => contactTolerance;
    public bool AlwaysActive => alwaysActive;
    public bool CanInteract => contactCollider is not CharacterController controller || controller.isGrounded;
    public Vector3 ContactPosition
    {
        get
        {
            if (!contactCollider) return transform.position;
            Bounds bounds = contactCollider.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
    }

    void Reset() => contactCollider = GetComponentInChildren<Collider>();

    void OnValidate() { if (!contactCollider) contactCollider = GetComponentInChildren<Collider>(); }

    public void Configure(Collider value, bool keepAlwaysActive = false)
    {
        contactCollider = value;
        SetAlwaysActive(keepAlwaysActive);
    }

    public void SetAlwaysActive(bool value) => alwaysActive = value;

    void OnEnable()
    {
        SurfaceContactSystem.RegisterAgent(this);
    }

    void OnDisable()
    {
        SurfaceContactSystem.UnregisterAgent(this);
        ClearContact();
    }

    public void SetContact(SurfaceContact value)
    {
        Contact = value;
        HasContact = true;
    }

    public void ClearContact()
    {
        if (!HasContact) return;
        HasContact = false;
        Contact = default;
    }
}
