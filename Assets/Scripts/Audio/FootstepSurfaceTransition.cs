using UnityEngine;

namespace HauntedHouse.Audio
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FootstepSurfaceTransition : MonoBehaviour
    {
        [SerializeField] private Vector3 alternateSide = Vector3.right;
        private BoxCollider trigger;

        private void Awake()
        {
            trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerExit(Collider other)
        {
            NavMeshFootstepAudio footsteps = other.GetComponentInParent<NavMeshFootstepAudio>();
            if (footsteps == null)
            {
                return;
            }

            Vector3 direction = transform.TransformDirection(alternateSide.normalized);
            bool onAlternateSide = Vector3.Dot(footsteps.transform.position - transform.position, direction) > 0f;
            footsteps.SetAlternate(onAlternateSide);
        }

        private void Reset()
        {
            trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }
    }
}
