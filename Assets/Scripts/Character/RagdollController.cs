using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles the two passive physics-feel pillars only — no ragdoll triggering.
///
///  1. NOODLE BALANCE  — a weak upright slerp drive on the hips resists tipping but yields
///     to external forces. NetworkPlayer reads GetScaledMainDrive() every fixed tick.
///
///  2. DYNAMIC STRENGTH SCALING  — as root speed increases the muscle strength fraction
///     scales down so the body goes partially limp during fast motion (inertia takes over).
///     Arm/leg joints follow the same scale so limbs flop naturally at high speed.
/// </summary>
public class RagdollController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Header("Noodle Balance")]
    [Tooltip("Spring strength of the mainJoint slerp drive that keeps the body upright. " +
             "Lower = wobblier. HFF uses roughly 600–900.")]
    [SerializeField] float uprightSpring = 700f;
    [Tooltip("Damper of the upright slerp drive.")]
    [SerializeField] float uprightDamper = 35f;

    [Header("Dynamic Strength Scaling")]
    [Tooltip("Root speed (m/s) at which muscles begin to scale down.")]
    [SerializeField] float velocityLimpThreshold = 4f;
    [Tooltip("Root speed (m/s) at which muscles reach minimum strength.")]
    [SerializeField] float velocityFullLimpSpeed = 9f;
    [Tooltip("Minimum muscle strength fraction (0–1) at full limp speed.")]
    [SerializeField] [Range(0f, 1f)] float minMuscleStrength = 0.08f;
    [Tooltip("How quickly the muscle scale smooths toward its target each fixed tick.")]
    [SerializeField] float muscleScaleSmoothing = 6f;

    [Header("Root references (auto-found if empty)")]
    [SerializeField] Rigidbody         rootRb;
    [SerializeField] ConfigurableJoint mainJoint;

    // ── State ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Current muscle strength fraction [0,1].
    /// NetworkPlayer reads this every fixed tick to scale the mainJoint slerp drive.
    /// </summary>
    public float MuscleStrength { get; private set; } = 1f;

    float _muscleScaleTarget = 1f;

    // ── Bone data ─────────────────────────────────────────────────────────────────

    struct BoneRecord
    {
        public ConfigurableJoint joint;
        public JointDrive        savedSlerpDrive;
    }

    readonly List<BoneRecord> _bones = new List<BoneRecord>();

    // ── Unity lifecycle ───────────────────────────────────────────────────────────

    void Awake()
    {
        if (rootRb    == null) rootRb    = GetComponent<Rigidbody>();
        if (mainJoint == null) mainJoint = GetComponent<ConfigurableJoint>();

        CollectBones();
        ApplyUprightDrive();
    }

    void FixedUpdate()
    {
        float speed = rootRb != null ? rootRb.linearVelocity.magnitude : 0f;

        _muscleScaleTarget = speed <= velocityLimpThreshold
            ? 1f
            : Mathf.Lerp(1f, minMuscleStrength,
                  Mathf.InverseLerp(velocityLimpThreshold, velocityFullLimpSpeed, speed));

        MuscleStrength = Mathf.Lerp(MuscleStrength, _muscleScaleTarget,
                                    muscleScaleSmoothing * Time.fixedDeltaTime);

        ApplyScaledLimbDrives(MuscleStrength);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the mainJoint slerp drive scaled by <paramref name="strengthFraction"/>.
    /// Called by NetworkPlayer every FixedUpdateNetwork.
    /// </summary>
    public JointDrive GetScaledMainDrive(float strengthFraction)
    {
        return new JointDrive
        {
            positionSpring = uprightSpring * strengthFraction,
            positionDamper = uprightDamper * Mathf.Sqrt(strengthFraction),
            maximumForce   = float.MaxValue
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    void ApplyUprightDrive()
    {
        if (mainJoint == null) return;
        mainJoint.slerpDrive = new JointDrive
        {
            positionSpring = uprightSpring,
            positionDamper = uprightDamper,
            maximumForce   = float.MaxValue
        };
    }

    void ApplyScaledLimbDrives(float t)
    {
        foreach (BoneRecord b in _bones)
        {
            if (b.joint == null) continue;
            JointDrive d = b.savedSlerpDrive;
            b.joint.slerpDrive = new JointDrive
            {
                positionSpring = d.positionSpring * t,
                positionDamper = d.positionDamper * Mathf.Sqrt(t),
                maximumForce   = float.MaxValue
            };
        }
    }

    void CollectBones()
    {
        _bones.Clear();
        ConfigurableJoint[] joints = GetComponentsInChildren<ConfigurableJoint>(true);
        foreach (ConfigurableJoint j in joints)
        {
            if (j == mainJoint) continue;
            _bones.Add(new BoneRecord
            {
                joint           = j,
                savedSlerpDrive = j.slerpDrive,
            });
        }
    }
}
