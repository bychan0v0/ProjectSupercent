using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[CreateAssetMenu(menuName="VFX/Item Travel Profile")]
public class ItemTravelProfile : ScriptableObject
{
    [Min(0.01f)] public float duration = 0.25f;
    [Min(0f)]     public float jumpHeight = 0.25f; // DOJump jumpPower
    public Ease    ease = Ease.OutCubic;
    public Vector3 spinEulerPerSec = new(0, 360, 0); // 선택 회전
    public bool    parentToTarget = false;
    public Vector3 endLocalOffset, endLocalEuler;
}
