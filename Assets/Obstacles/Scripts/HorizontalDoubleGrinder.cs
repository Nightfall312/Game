using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
public class HorizontalDoubleGrinder : MonoBehaviour
{
    [SerializeField] private Transform grinder01;
    [SerializeField] private Transform grinder02;
    [SerializeField] private float duration;
    Vector3 rotation = new Vector3(360, 0, 0);

    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartAnimation();
    }

    private void StartAnimation()
    {
        grinder01.DOLocalRotate(rotation,duration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        grinder02.DOLocalRotate(-rotation,duration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);

    }}
}
