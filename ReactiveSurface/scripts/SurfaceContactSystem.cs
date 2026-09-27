using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class SurfaceContactSystem : MonoBehaviour
{
    static SurfaceContactSystem instance;

    [SerializeField] Transform focus;
    [SerializeField, Min(4f)] float cellSize = 16f;
    [SerializeField, Min(0f)] float npcEnterDistance = 12f;
    [SerializeField, Min(0f)] float npcExitDistance = 15f;
    [SerializeField, Min(0.01f)] float sampleInterval = 0.1f;
    [SerializeField, Min(0.05f)] float relevanceInterval = 0.2f;
    [SerializeField, Min(0.001f)] float movementThreshold = 0.05f;

    readonly Dictionary<SurfaceContactAgent, ContactSchedule> agents = new();
    readonly HashSet<IContactSurface> surfaces = new();
    readonly Dictionary<Vector2Int, List<IContactSurface>> grid = new();
    bool gridDirty = true;

    public static void RegisterAgent(SurfaceContactAgent agent)
    {
        if (!instance)
        {
            Debug.LogError("Scene에 SurfaceContactSystem이 필요합니다.", agent);
            return;
        }

        if (!instance.agents.ContainsKey(agent)) instance.agents.Add(agent, new ContactSchedule(agent));
    }

    public static void UnregisterAgent(SurfaceContactAgent agent)
    {
        if (instance) instance.agents.Remove(agent);
    }

    public static void RegisterSurface(IContactSurface surface)
    {
        if (!instance)
        {
            Debug.LogError("Scene에 SurfaceContactSystem이 필요합니다.", surface.Component);
            return;
        }

        instance.surfaces.Add(surface);
        instance.gridDirty = true;
    }

    public static void UnregisterSurface(IContactSurface surface)
    {
        if (!instance) return;
        instance.surfaces.Remove(surface);
        instance.gridDirty = true;
    }

    public static void SetFocus(Transform value)
    {
        if (instance) instance.focus = value;
    }

    void Awake()
    {
        if (instance && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (gridDirty) RebuildGrid();

        float now = Time.time;
        Vector3 focusPosition = focus ? focus.position : Vector3.zero;
        float enterSqr = npcEnterDistance * npcEnterDistance;
        float exitDistance = Mathf.Max(npcEnterDistance, npcExitDistance);
        float exitSqr = exitDistance * exitDistance;
        float movementSqr = movementThreshold * movementThreshold;

        foreach (KeyValuePair<SurfaceContactAgent, ContactSchedule> pair in agents)
        {
            SurfaceContactAgent agent = pair.Key;
            if (!agent || !agent.isActiveAndEnabled) continue;

            ContactSchedule schedule = pair.Value;
            Vector3 position = agent.ContactPosition;
            float distanceSqr = focus ? (position - focusPosition).sqrMagnitude : 0f;

            if (!agent.AlwaysActive)
            {
                UpdateRelevance(agent, schedule, distanceSqr, enterSqr, exitSqr, now);
                if (!schedule.IsRelevant) continue;
            }

            bool moved = (position - schedule.LastPosition).sqrMagnitude >= movementSqr;
            if (!agent.AlwaysActive && !moved && now < schedule.NextSampleTime) continue;

            schedule.LastPosition = position;
            schedule.NextSampleTime = now + sampleInterval;
            Sample(agent, position);
        }
    }

    void UpdateRelevance(SurfaceContactAgent agent, ContactSchedule schedule, float distanceSqr, float enterSqr, float exitSqr, float now)
    {
        if (now < schedule.NextRelevanceTime) return;

        schedule.NextRelevanceTime = now + relevanceInterval;
        bool relevant = !focus || distanceSqr <= (schedule.IsRelevant ? exitSqr : enterSqr);
        if (schedule.IsRelevant && !relevant) agent.ClearContact();
        schedule.IsRelevant = relevant;
    }

    void Sample(SurfaceContactAgent agent, Vector3 position)
    {
        if (!grid.TryGetValue(ToCell(position), out List<IContactSurface> candidates))
        {
            agent.ClearContact();
            return;
        }

        bool found = false;
        SurfaceContact best = default;
        int bestPriority = int.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            IContactSurface surface = candidates[i];
            if (surface.Component == null || !surface.ContactBounds.Contains(position)) continue;
            if (!surface.TrySample(position, out SurfaceContact sample) || sample.Depth < -agent.ContactTolerance) continue;

            bool isHigher = sample.Point.y > best.Point.y + 0.01f;
            bool isSameHeight = Mathf.Abs(sample.Point.y - best.Point.y) <= 0.01f;
            if (!found || isHigher || isSameHeight && surface.Priority > bestPriority)
            {
                found = true;
                best = sample;
                bestPriority = surface.Priority;
            }
        }

        if (found) agent.SetContact(best);
        else agent.ClearContact();
    }

    void RebuildGrid()
    {
        grid.Clear();

        foreach (IContactSurface surface in surfaces)
        {
            if (surface.Component == null) continue;

            Bounds bounds = surface.ContactBounds;
            Vector2Int min = ToCell(bounds.min);
            Vector2Int max = ToCell(bounds.max);

            for (int z = min.y; z <= max.y; z++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector2Int cell = new(x, z);
                    if (!grid.TryGetValue(cell, out List<IContactSurface> list))
                    {
                        list = new List<IContactSurface>(2);
                        grid.Add(cell, list);
                    }

                    list.Add(surface);
                }
            }
        }

        gridDirty = false;
    }

    Vector2Int ToCell(Vector3 position) => new(
        Mathf.FloorToInt(position.x / cellSize),
        Mathf.FloorToInt(position.z / cellSize));

    sealed class ContactSchedule
    {
        public float NextSampleTime;
        public float NextRelevanceTime;
        public Vector3 LastPosition;
        public bool IsRelevant;

        public ContactSchedule(SurfaceContactAgent agent)
        {
            NextRelevanceTime = Time.time + Mathf.Abs(agent.GetInstanceID() % 20) * 0.01f;
            LastPosition = agent.ContactPosition;
            IsRelevant = agent.AlwaysActive;
        }
    }
}
