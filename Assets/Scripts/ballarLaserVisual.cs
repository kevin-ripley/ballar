using UnityEngine;


    public class ballarLaserVisual : MonoBehaviour
    {
        [Header("System References")]
        [SerializeField] private ballarLaser m_Laser; // Assign your LaserPointer object
        [SerializeField] private LineRenderer m_LineRenderer;

        private void Awake()
        {
            if (m_LineRenderer == null)
                m_LineRenderer = GetComponent<LineRenderer>();
        }

        private void LateUpdate()
        {
            if (m_Laser == null || m_LineRenderer == null) return;

            if (m_Laser.IsHitting)
            {
                m_LineRenderer.enabled = true;
                // Start of the line is the laser's own position
                m_LineRenderer.SetPosition(0, m_Laser.transform.position);
                // End of the line is the hit point
                m_LineRenderer.SetPosition(1, m_Laser.CurrentHit.point);
            }
            else
            {
                m_LineRenderer.enabled = false;
            }
        }
    }
