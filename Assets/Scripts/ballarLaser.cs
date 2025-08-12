// ballarLaser.cs
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ballarLaser : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private LayerMask m_InteractionLayerMask = ~0;
    [SerializeField] private float m_MaxRaycastDistance = 20f;
    [SerializeField] private InputActionProperty m_TriggerAction;

    [SerializeField] private Transform m_RayOriginTransform; // <- assign Right Controller tip / pointer origin
    [SerializeField] private LineRenderer m_LineRenderer;    // optional (can be null)

    public bool IsHitting { get; private set; }
    public RaycastHit CurrentHit { get; private set; }

    public UnityEvent<Vector3> OnPointSelected;

    void OnEnable()
    {
        if (m_TriggerAction.action != null)
        {
            m_TriggerAction.action.Enable();
            m_TriggerAction.action.performed += TriggerActionPerformed;
        }

        if (m_LineRenderer != null)
        {
            m_LineRenderer.useWorldSpace = true;
            m_LineRenderer.positionCount = 2;
            m_LineRenderer.enabled = false;
        }
    }

    void OnDisable()
    {
        if (m_TriggerAction.action != null)
            m_TriggerAction.action.performed -= TriggerActionPerformed;
    }

    void Update()
    {
        var origin = m_RayOriginTransform != null ? m_RayOriginTransform.position : transform.position;
        var dir    = m_RayOriginTransform != null ? m_RayOriginTransform.forward  : transform.forward;

        IsHitting = Physics.Raycast(origin, dir, out RaycastHit hit, m_MaxRaycastDistance, m_InteractionLayerMask);
        if (IsHitting)
            CurrentHit = hit;

        if (m_LineRenderer != null)
        {
            if (IsHitting)
            {
                m_LineRenderer.enabled = true;
                m_LineRenderer.SetPosition(0, origin);
                m_LineRenderer.SetPosition(1, CurrentHit.point);
            }
            else
            {
                m_LineRenderer.enabled = false;
            }
        }
    }

    void TriggerActionPerformed(InputAction.CallbackContext ctx)
    {
        if (IsHitting)
            OnPointSelected?.Invoke(CurrentHit.point);
    }
}
