using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hyper_Casuel_Obstacle
{
    public class HammerAnimation : MonoBehaviour
    {
        [SerializeField] private Transform hammer;
        [SerializeField] private Vector3 rotationVector;
        [SerializeField] private float duraction;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            StartHammerAnimation();
        }
        private void StartHammerAnimation()
        {
            hammer.DOLocalRotate(rotationVector, duraction * speedMultiplier, RotateMode.Fast)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.OutQuint);
        }
    }
}