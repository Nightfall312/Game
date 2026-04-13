using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class AxeHorizontal01Animation : MonoBehaviour
    {
        [SerializeField] private Transform axe;

        [SerializeField] private float duraction;

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            var newRotation = new Vector3(0, 360, 0);
            axe.DOLocalRotate(newRotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
