using System;
using System.Collections.Generic;
using PX;
using UnityEngine;

/// <summary>
/// 룬 소켓 데이터 (JSON 직렬화용)
/// Unity JsonUtility는 Dictionary를 직렬화하지 못하므로 Wrapper 클래스 사용
/// </summary>
[Serializable]
public class RuneSocketEntry
{
    public int socketIndex;
    public ESkill rune;
}

/// <summary>
/// 배틀 시뮬레이터 프리셋 데이터 클래스
/// JSON 직렬화를 통해 장비/스킬/성좌 설정을 저장하고 불러옵니다.
/// </summary>
[Serializable]
public class SimulatorPresetData
{
    /// <summary>
    /// 프리셋 이름
    /// </summary>
    public string presetName;

    // ==================== 일반 장비 9슬롯 (~전설) ====================

    /// <summary>
    /// 무기 (~전설)
    /// </summary>
    public EEquipmentNormal weapon;

    /// <summary>
    /// 투구 (~전설)
    /// </summary>
    public EEquipmentNormal helmet;

    /// <summary>
    /// 갑옷 (~전설)
    /// </summary>
    public EEquipmentNormal bodyArmor;

    /// <summary>
    /// 장갑 (~전설)
    /// </summary>
    public EEquipmentNormal gloves;

    /// <summary>
    /// 신발 (~전설)
    /// </summary>
    public EEquipmentNormal boots;

    /// <summary>
    /// 목걸이 (~전설)
    /// </summary>
    public EEquipmentNormal amulet;

    /// <summary>
    /// 벨트 (~전설)
    /// </summary>
    public EEquipmentNormal belt;

    /// <summary>
    /// 반지 1 (~전설)
    /// </summary>
    public EEquipmentNormal ring1;

    /// <summary>
    /// 반지 2 (~전설)
    /// </summary>
    public EEquipmentNormal ring2;

    // ==================== 신화 장비 9슬롯 (신화) ====================

    /// <summary>
    /// 무기 (신화)
    /// </summary>
    public EEquipmentMythic weaponMythic;

    /// <summary>
    /// 투구 (신화)
    /// </summary>
    public EEquipmentMythic helmetMythic;

    /// <summary>
    /// 갑옷 (신화)
    /// </summary>
    public EEquipmentMythic bodyArmorMythic;

    /// <summary>
    /// 장갑 (신화)
    /// </summary>
    public EEquipmentMythic glovesMythic;

    /// <summary>
    /// 신발 (신화)
    /// </summary>
    public EEquipmentMythic bootsMythic;

    /// <summary>
    /// 목걸이 (신화)
    /// </summary>
    public EEquipmentMythic amuletMythic;

    /// <summary>
    /// 벨트 (신화)
    /// </summary>
    public EEquipmentMythic beltMythic;

    /// <summary>
    /// 반지 1 (신화)
    /// </summary>
    public EEquipmentMythic ring1Mythic;

    /// <summary>
    /// 반지 2 (신화)
    /// </summary>
    public EEquipmentMythic ring2Mythic;

    // ==================== 스킬 3슬롯 ====================

    /// <summary>
    /// 메인 스펠
    /// </summary>
    public ESkill mainSpell;

    /// <summary>
    /// 메인 스펠 티어
    /// </summary>
    public ETier spellTier;

    /// <summary>
    /// 메인 스펠 등급 (mythic만 사용, legendary 이하는 grade1 고정)
    /// </summary>
    public EGrade spellGrade;

    /// <summary>
    /// 스킬룬 소켓 (주문에 장착) - List로 저장 (JsonUtility는 Dictionary 직렬화 불가)
    /// </summary>
    public List<RuneSocketEntry> runeSockets;

    /// <summary>
    /// 오라
    /// </summary>
    public ESkill aura;

    /// <summary>
    /// 오라 등급
    /// </summary>
    public ETier auraTier;

    /// <summary>
    /// 펫
    /// </summary>
    public EPet pet;

    /// <summary>
    /// 펫 등급
    /// </summary>
    public ETier petTier;

    // ==================== 성좌 (Constellation) ====================

    /// <summary>
    /// [하위호환] 기존 단일 성좌 필드 (기존 JSON 역직렬화용, 신규 저장 시 None 유지)
    /// </summary>
    public EConstellation selectedConstellation;

    /// <summary>
    /// 성좌 슬롯 1 (기본)
    /// </summary>
    public EConstellation constellationSlot1;

    /// <summary>
    /// 성좌 슬롯 2 (성좌 5종 보유 시 해금)
    /// </summary>
    public EConstellation constellationSlot2;

    /// <summary>
    /// 성좌 슬롯 3 (모든 성좌 보유 시 해금)
    /// </summary>
    public EConstellation constellationSlot3;

    // ==================== 최적화 설정 ====================

    /// <summary>
    /// 빌드 타입 (Hit 빌드 / Ailment 빌드)
    /// </summary>
    public BattleSimulator.BuildType buildType;

    // ==================== 조건 설정 ====================

    /// <summary>
    /// Full Life 상태 (HP 100%)
    /// </summary>
    public bool isFullLife;

    /// <summary>
    /// Low Life 상태 (HP ≤ 35%)
    /// </summary>
    public bool isLowLife;

    /// <summary>
    /// 최근에 적 처치 상태
    /// </summary>
    public bool hasKilledRecently;

    // 적 상태이상 조건
    /// <summary>
    /// 적이 Bleeding 상태이상에 걸림
    /// </summary>
    public bool targetHasBleeding;

    /// <summary>
    /// 적이 Ignite 상태이상에 걸림
    /// </summary>
    public bool targetHasIgnite;

    /// <summary>
    /// 적이 Arctic 상태이상에 걸림
    /// </summary>
    public bool targetHasArctic;

    /// <summary>
    /// 적이 Chill 상태이상에 걸림
    /// </summary>
    public bool targetHasChill;

    /// <summary>
    /// 적이 Shock 상태이상에 걸림
    /// </summary>
    public bool targetHasShock;

    /// <summary>
    /// 적이 Paralyze 상태이상에 걸림
    /// </summary>
    public bool targetHasParalyze;

    /// <summary>
    /// 적이 Poisoning 상태이상에 걸림
    /// </summary>
    public bool targetHasPoisoning;

    /// <summary>
    /// 적이 Stun 상태이상에 걸림
    /// </summary>
    public bool targetHasStun;
}
