using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 방에 배치되는 CCTV 가상 카메라다.
/// 실제 렌더링은 Main Camera 하나가 담당하고, 이 컴포넌트는 시점 프리셋만 제공한다.
/// </summary>
public class RoomCamera : MonoBehaviour
{
    [SerializeField] private CinemachineCamera virtualCamera;

    public CinemachineCamera VirtualCamera => virtualCamera;

    private void Reset()
    {
        virtualCamera = GetComponent<CinemachineCamera>();
    }
}
