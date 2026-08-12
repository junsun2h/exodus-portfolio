using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    public delegate void Delegate_ChangeHP(double InHP, FCalcResultModDamage InDamageData = null);
    public delegate void Delegate_ChangeMP(double InMP);



    public class FBattleStatus : IPXDisposable
    {
        // 성능 최적화: Enum.GetValues 캐싱 (매번 새 배열 생성 방지)
        private static readonly EMod[] _cachedEModValues = (EMod[])System.Enum.GetValues(typeof(EMod));
        private static readonly ESkillMod[] _cachedESkillModValues = (ESkillMod[])System.Enum.GetValues(typeof(ESkillMod));
        private static readonly ECombineMod[] _cachedECombineModValues = (ECombineMod[])System.Enum.GetValues(typeof(ECombineMod));

        // Ailment CC 효과 상수
        // Chill (냉각) - 동작 속도 감소
        // 위키: https://realspace3.atlassian.net/wiki/spaces/Pixel/pages/.../ailment
        private const double CHILL_BASE_EFFECT = 0.20;  // 기본 효과 20%
        private const double CHILL_MAX_EFFECT = 0.50;   // 최대 효과 50%

        public Delegate_ChangeHP DelegateHP;
        public Delegate_ChangeMP DelegateMP;
        public ECharacterSingleDataType CharacterSingleDataType { get; protected set; }
        public bool IsBattleOn { get; private set; }

        // 배틀 시뮬레이터 조건부 설정 (에디터 전용)
        public bool SimulateFullLife { get; set; } = false;
        public bool SimulateLowLife { get; set; } = false;
        public bool SimulateKilledRecently { get; set; } = false;

        // HP 상태 체크 (시뮬레이터 설정 우선 적용)
        public bool IsHPMax => SimulateFullLife || resultHp.Value >= resultHpMax.Value;
        public bool IsHPHalf => SimulateLowLife || resultHp.Value <= resultHpMax.Value * 0.5;
        public bool IsMPMax => resultMp.Value >= resultMpMax.Value;
        public bool IsAblePassive { get; private set; }

        // 최근 처치 추적 (mod_*_when_kill_recently 지원)
        private float lastKillTime = -999f;
        public bool HasKilledRecently => SimulateKilledRecently || (Time.time - lastKillTime) <= 4f;

        /// <summary>
        /// Result ���� ���� ����
        /// </summary>
        /// Result
        public CryptoValueDouble resultHp { get; private set; }
        public CryptoValueDouble resultMp { get; private set; }
        public CryptoValueDouble originHpMax { get; private set; }
        public CryptoValueDouble originMpMax { get; private set; }
        public CryptoValueDouble resultHpMax { get; private set; }
        public CryptoValueDouble resultMpMax { get; private set; }

        // 저항 최대치 (기본값 75%)
        private double fireResistanceMax = 75.0;
        private double coldResistanceMax = 75.0;
        private double lightningResistanceMax = 75.0;
        private double poisonResistanceMax = 75.0;
        private double physicalResistanceMax = 75.0;

        //public int resultNormalAttackSpeed { get { return 100;/*originNormalAttackSpeed + buffData.normalAttackSpeed;*/ } }

        public int ResultBattlePoint { get; private set; }
        public FBattleStatusDataBase characterData { get; private set; } //Player, Monster
        public FBattleStatusEquipData equipData { get; private set; }
        public FBattleStatusSkillData skillData { get; private set; }
        public FBattleStatusBuffData buffData { get; private set; }

        public FCharacterStatus GetCharacterStatus
        {
            get
            {
                return GameCharacterManager.Instance.GetCharacterStatusByUID(characterSingleData.CUID);
            }
        }
        public UCharacterActor GetCharacterActor
        {
            get
            {
                return GetCharacterStatus?.CharacterActor;
            }
        }
        public FBattleCombineModData AllCombineModData => _allCombineModData;
        public FBattleCombineModData AllCombineBattleModData => _allCombineBattleModData;

        //집계 딕셔너리 초기 용량. 0이 아닌 mod 만 저장하므로 enum 전체 개수(271/26/50)가 아니라
        //실제로 값이 들어차는 규모에 맞춘다. 넘어가도 Dictionary 가 알아서 늘어난다.
        //
        //PlayMode 실측(60마리 웨이브): 몬스터 EMod 10 / ESkillMod 0 / ECombineMod 0, 플레이어 59 / 0 / 9.
        //웨이브당 60마리가 만들어지는 몬스터 기준으로 맞춘다 — 플레이어는 1명뿐이라 몇 번 성장해도 무시할 만하다.
        //0으로 두면 Dictionary 가 첫 Add 전까지 배열을 아예 할당하지 않는다
        private const int ModDataInitialCapacity = 16;
        private const int SkillModDataInitialCapacity = 0;
        private const int CombineModDataInitialCapacity = 0;

        private FBattleModData _allBattleModData;
        private FBattleModSkillData _allBattleModSkillData;
        private FBattleCombineModData _allCombineModData;
        private FBattleCombineModData _allCombineBattleModData;
        private FCharacterSingleData characterSingleData;



        public FBattleStatus(FCharacterSingleData InBasicStatus)
        {
            if (InBasicStatus.CharacterSingleDataType == ECharacterSingleDataType.None)
            {
                Debug.LogError("FBattleStatus CharacterSingleDataType None");
                return;
            }

            if (InBasicStatus.CharacterSingleDataType == ECharacterSingleDataType.Player)
            {
                characterData = FBattleStatusPlayerData.NewStatusData(InBasicStatus);
            }
            else if (InBasicStatus.CharacterSingleDataType == ECharacterSingleDataType.Monster)
            {
                characterData = FBattleStatusMonsterData.NewStatusData(InBasicStatus);
            }
            else if (InBasicStatus.CharacterSingleDataType == ECharacterSingleDataType.Pet)
            {
                characterData = FBattleStatusPetData.NewStatusData(InBasicStatus);
            }
            else
            {
                Debug.LogError("FBattleStatus CharacterSingleDataType Error");
            }


            //0값을 저장하지 않게 되면서 실제 엔트리는 수십 개 수준이다.
            //성장하며 중간 배열을 버리지 않을 만큼만 미리 잡는다 (실측 후 조정 가능)
            _allBattleModData = new FBattleModData(InBasicStatus, ModDataInitialCapacity);
            _allBattleModSkillData = new FBattleModSkillData(InBasicStatus, SkillModDataInitialCapacity);
            _allCombineModData = new FBattleCombineModData(InBasicStatus, CombineModDataInitialCapacity);
            _allCombineBattleModData = new FBattleCombineModData(InBasicStatus, CombineModDataInitialCapacity);

            characterSingleData = InBasicStatus;
            CharacterSingleDataType = InBasicStatus.CharacterSingleDataType;
            equipData = FBattleStatusEquipData.NewStatusData(InBasicStatus);
            skillData = FBattleStatusSkillData.NewStatusData(InBasicStatus);
            buffData = FBattleStatusBuffData.NewStatusData(InBasicStatus);

            UpdateBattleStatus(InBasicStatus);

            //과거에는 여기서 자기 자신의 ChangedHP/ChangedMP 를 구독시켰는데,
            //그 핸들러가 다시 UpdateHP/UpdateMP 를 부르는 자기 재귀라 값이 NaN 이 되는 순간
            //종료 조건(tempHp == InValue)이 성립하지 않아 StackOverflow 로 이어졌다.
            //재구독해도 값만 한 번 더 검사할 뿐 아무 효과가 없어 제거하고, 호출부는 null 안전 호출로 바꿨다
        }
        public FSpellSkillData GetSpellSkillData()
        {
            return skillData.GetSpellSkillData();
        }


        public void UpdateBattleStatus(FCharacterSingleData InBasicStatus)
        {
            if (InBasicStatus.CharacterSingleDataType == ECharacterSingleDataType.Player)
            {
            }

            characterData.UpdateStatus();
            equipData.UpdateStatus();
            skillData.UpdateStatus();
            /*Buff�� �̹� �ϼ��� ������ �߰��ɰ��̹Ƿ� �߰��� UpdateStatus �ʿ����� ����*/

            // min MOD가 있는 버프는 버프가 없어도 최소 스택 MOD 적용 (시뮬레이터 로직과 동일)
            AutoApplyMinBuffModifiers(InBasicStatus);

            UpdateAllData();

            //�������� �������� hp/mp ����
            originHpMax = CryptoValueDouble.Create(ResultMaxLife(false)); //CryptoValueDouble.Create(TotalModValue(EMod.mod_life, false) + TotalModValue(EMod.mod_life_inc, false));
            originMpMax = CryptoValueDouble.Create(ResultMaxMana(false)); //CryptoValueDouble.Create(TotalModValue(EMod.mod_mana, false) + TotalModValue(EMod.mod_mana_inc, false));

            //MAX hp/mp ����
            resultHpMax = CryptoValueDouble.Create(ResultMaxLife());//CryptoValueDouble.Create(TotalModValue(EMod.mod_life) + TotalModValue(EMod.mod_life_inc));
            resultMpMax = CryptoValueDouble.Create(ResultMaxMana()); //CryptoValueDouble.Create(TotalModValue(EMod.mod_mana) + TotalModValue(EMod.mod_mana_inc));

            //�ΰ��� hp/mp ����
            resultHp = resultHpMax;
            resultMp = resultMpMax;

            // 저항 최대치 계산
            CalculateResistanceMax();
        }

        /// <summary>
        /// 네 갈래 소스(캐릭터/장비/스킬/버프)의 mod 를 합쳐 집계 딕셔너리에 채웁니다.
        ///
        /// 몬스터 1마리 소환마다 EMod 271 + ESkillMod 26 + ECombineMod 50 = 347회 순회한다.
        /// 값이 0인 mod 는 저장하지 않고 기존 엔트리를 지운다 — GetModValue()가 키 부재 시 0을 돌려주므로
        /// 읽는 쪽 의미는 완전히 같고(BattleStatusData.GetModValue), 엔트리 수는 수십 개로 줄어든다.
        /// 집계 딕셔너리에 대고 IsExistModValue()를 부르는 코드는 없다 (호출은 전부 소스 데이터 대상)
        /// </summary>
        private void UpdateAllData()
        {
            // 성능 최적화: 캐싱된 Enum 배열 사용
            for (int i = 0; i < _cachedEModValues.Length; i++)
            {
                EMod mod = _cachedEModValues[i];
                if (mod == EMod.None) continue;  // None은 미설정 값이므로 건너뜀

                double resultValue = 0;
                if (characterData.IsExistModValue(mod)) { resultValue += characterData.GetModValue(mod).Value; }
                if (equipData.IsExistModValue(mod)) { resultValue += equipData.GetModValue(mod).Value; }
                if (skillData.IsExistModValue(mod)) { resultValue += skillData.GetModValue(mod).Value; }
                if (buffData.IsExistModValue(mod)) { resultValue += buffData.GetModValue(mod).Value; }

                if (resultValue != 0)
                    _allBattleModData.SetModValue(mod, CryptoValueDouble.Create(resultValue));
                else
                    _allBattleModData.RemoveModValue(mod);   //이전 값이 남지 않도록 지운다
            }

            for (int i = 0; i < _cachedESkillModValues.Length; i++)
            {
                ESkillMod mod = _cachedESkillModValues[i];
                if (mod == ESkillMod.None) continue;  // None은 미설정 값이므로 건너뜀

                double resultValue = 0;
                if (characterData.IsExistModValue(mod)) { resultValue += characterData.GetModValue(mod).Value; }
                if (equipData.IsExistModValue(mod)) { resultValue += equipData.GetModValue(mod).Value; }
                if (skillData.IsExistModValue(mod)) { resultValue += skillData.GetModValue(mod).Value; }
                if (buffData.IsExistModValue(mod)) { resultValue += buffData.GetModValue(mod).Value; }

                if (resultValue != 0)
                    _allBattleModSkillData.SetModValue(mod, CryptoValueDouble.Create(resultValue));
                else
                    _allBattleModSkillData.RemoveModValue(mod);
            }

            for (int i = 0; i < _cachedECombineModValues.Length; i++)
            {
                ECombineMod mod = _cachedECombineModValues[i];
                if (mod == ECombineMod.None) continue;  // None은 미설정 값이므로 건너뜀

                double resultValue = 0;
                if (characterData.IsExistModValue(mod)) { resultValue += characterData.GetModValue(mod).Value; }
                if (equipData.IsExistModValue(mod)) { resultValue += equipData.GetModValue(mod).Value; }
                if (skillData.IsExistModValue(mod)) { resultValue += skillData.GetModValue(mod).Value; }
                if (buffData.IsExistModValue(mod)) { resultValue += buffData.GetModValue(mod).Value; }

                if (resultValue != 0)
                    _allCombineModData.SetModValue(mod, CryptoValueDouble.Create(resultValue));
                else
                    _allCombineModData.RemoveModValue(mod);
            }

            // foreach (var item in _allCombineModData.CombineModDBDic)
            // {
            //     ECombineMod eCombineMod = item.Key;
            //     GameDB_Client_CombineMod combineModDB = item.Value;
            //     if (combineModDB != null)
            //     {
            //         CryptoValueDouble combineModValue = _allCombineModData.GetModValue(eCombineMod);
            //         _allCombineBattleModData.AddCombineBattleMod(combineModDB.Buff, combineModDB.Mod, combineModValue);
            //     }
            // }
        }

        //CopyAllModValue / CopyAllCombineModValue / CopyAllCombineBattleModValue 는 제거했다.
        //유일한 호출자였던 FSkillData 의 mod 스냅샷을 읽는 코드가 없어 함께 걷어냈다

        public void SetDeadStatus()
        {
            IsBattleOn = false;

            resultHp = CryptoValueDouble.Create(0.0);
            resultMp = CryptoValueDouble.Create(0.0);
            //resultHp.UpdateValue(0);
            //resultMp.UpdateValue(0);
        }

        public void SetReviveStatus()
        {
            resultHp = resultHpMax;
            resultMp = resultMpMax;
        }

        public void UpdateBateleOn(bool InBattleOn)
        {
            IsBattleOn = InBattleOn;
        }

        /// <summary>
        /// 적 처치 시 호출 (mod_*_when_kill_recently 지원 + 체력/마나 회복)
        /// </summary>
        public void OnEnemyKilled()
        {
            lastKillTime = Time.time;

            // 적 처치 시 체력 회복
            double recoverLife = TotalModValue(EMod.mod_recover_life_when_kill);
            if (recoverLife > 0)
            {
                GetCharacterActor.SetHealHP(recoverLife);
            }

            // 적 처치 시 마나 회복
            double recoverMana = TotalModValue(EMod.mod_recover_mana_when_kill);
            if (recoverMana > 0)
            {
                GetCharacterActor.SetHealMana(recoverMana);
            }
        }

        public void AddHP(double InValue, FCalcResultModDamage InDamageData = null)
        {
            //NaN/무한대는 여기서 끊는다. 흘려보내면 아래 UpdateHP 의 종료 조건이 무너진다
            if (double.IsFinite(InValue) == false)
            {
                Debug.LogError($"AddHP 비정상 값 차단, InValue = {InValue}, CUID = {characterSingleData.CUID}, Skill = {InDamageData?.SkillData?.Skill}");
                return;
            }

            UpdateHP(resultHp.Value + InValue, false, InDamageData);
        }

        public void UpdateHP(double InValue, bool InMaxChange = false, FCalcResultModDamage InDamageData = null)
        {
            //NaN 은 자기 자신과도 같지 않아서 아래 tempHp == InValue 종료 조건이 영원히 성립하지 않는다.
            //HP 갱신 통지를 받은 쪽이 다시 UpdateHP 를 부르는 구조라 그대로 두면 무한 재귀(StackOverflow)가 된다
            if (double.IsFinite(InValue) == false)
            {
                Debug.LogError($"UpdateHP 비정상 값 차단, InValue = {InValue}, CUID = {characterSingleData.CUID}, Skill = {InDamageData?.SkillData?.Skill}");
                return;
            }

            double tempHp = resultHp.Value;
            if (tempHp == InValue && InMaxChange == false)
                return;

            tempHp = InValue;
            double tempHpMax = resultHpMax.Value;

            if (tempHp < 0)
                tempHp = 0;
            else
            {
                if (InMaxChange)
                {
                    resultHpMax = CryptoValueDouble.Create(InValue);
                    tempHp = InValue;
                }
                else
                {
                    if (tempHp > tempHpMax)
                    {
                        tempHp = tempHpMax;
                    }
                }
            }

            //resultHp.UpdateValue(tempHp);
            resultHp = CryptoValueDouble.Create(tempHp);
            DelegateHP?.Invoke(tempHp, InDamageData);
        }

        public void AddMP(double InValue)
        {
            UpdateMP(resultMp.Value + InValue);
        }

        public void UpdateMP(double InValue)
        {
            //UpdateHP 와 같은 이유로 비정상 값을 흘려보내지 않는다
            if (double.IsFinite(InValue) == false)
            {
                Debug.LogError($"UpdateMP 비정상 값 차단, InValue = {InValue}, CUID = {characterSingleData.CUID}");
                return;
            }

            double tempMp = resultMp.Value;
            if (tempMp == InValue)
                return;

            tempMp = InValue;
            double tempMpMax = resultMpMax.Value;

            if (tempMp < 0)
                tempMp = 0;
            else if (tempMp > tempMpMax)
                tempMp = tempMpMax;

            //resultMp.UpdateValue(tempMp);
            resultMp = CryptoValueDouble.Create(tempMp);
            DelegateMP?.Invoke(tempMp);
        }


        protected override void ManagedDispose()
        {
            DelegateHP = null;
            DelegateMP = null;

            characterData?.Dispose();
            characterData = null;

            equipData?.Dispose();
            equipData = null;

            skillData?.Dispose();
            skillData = null;

            buffData?.Dispose();
            buffData = null;
        }

        /// <summary>
        /// TotalXXX ���� Mod�ջ� ���
        /// </summary>
        /// <param name="InType"></param>
        /// <param name="InWithBuff"></param>
        /// <returns></returns>

        public double TotalModValue(EMod InType, bool InWithBuff = true)
        {
            double result = _allBattleModData.GetModValue(InType).Value;

            {
                //콤바인모드 추가 적용
                result += _allCombineBattleModData.GetModValue(InType).Value;
            }

            if (InWithBuff == false)
            {
                result -= buffData.GetModValue(InType).Value;
            }

            return result;
        }
        public double TotalModValue(ESkillMod InType, bool InWithBuff = true)
        {
            double result = _allBattleModSkillData.GetModValue(InType).Value;

            if (InWithBuff == false)
            {
                result -= buffData.GetModValue(InType).Value;
            }

            return result;
        }

        /// <summary>
        /// ResultXXX ���� Mod ���� ���� ���
        /// </summary>
        /// <param name="InType"></param>
        /// <param name="InWithBuff"></param>
        /// <returns></returns>

        private double ResultAllDamage(double InData, double InInc, double InMore)
        {
            var damage = TotalModValue(EMod.mod_all_damage) + InData;
            var inc = TotalModValue(EMod.mod_all_damage_inc) + InInc;
            var more = TotalModValue(EMod.mod_all_damage_more) + InMore;

            // 조건부 피해 MOD 적용
            // 최대 생명력(Full Life): HP 100%
            // 낮은 생명력(Low Life): HP < 50%
            if (IsHPMax)
            {
                inc += TotalModValue(EMod.mod_damage_inc_on_full_life);
            }

            if (IsHPHalf)
            {
                inc += TotalModValue(EMod.mod_damage_inc_on_low_life);
            }

            var result = GameUtility.CalcResultMod(damage, inc, more);
            return result;

        }
        private double ResultAllSkillDamage(double InData, double InInc, double InMore = 0)
        {
            var damage = TotalModValue(EMod.mod_all_skill_damage) + InData;
            var inc = TotalModValue(EMod.mod_all_skill_damage_inc) + InInc;
            var more = TotalModValue(EMod.mod_all_skill_damage_more) + InMore;
            var result = ResultAllDamage(damage, inc, more);
            return result;

        }
        private double ResultElementalDamage(double InData, double InInc, double InMore = 0)
        {
            var damage = TotalModValue(EMod.mod_elemental_damage) + InData;
            var inc = TotalModValue(EMod.mod_elemental_damage_inc) + InInc;
            var result = ResultAllSkillDamage(damage, inc, InMore);
            return result;

        }
        // ========================================
        // 새로운 ModCalculator 기반 메서드
        // ========================================

        /// <summary>
        /// Physical 데미지 Calculator 반환 (새로운 방식)
        /// - Flat, Inc, More를 개별적으로 수집
        /// - Calculate() 호출 전까지 계산하지 않음
        /// </summary>
        public Game.Battle.ModCalculator GetPhysicalDamageCalculator()
        {
            var calc = new Game.Battle.ModCalculator()
                // Flat 수집
                .AddFlat(TotalModValue(EMod.mod_physical_damage))
                .AddFlat(TotalModValue(EMod.mod_all_skill_damage))
                .AddFlat(TotalModValue(EMod.mod_all_damage))

                // Increased 수집 (합산)
                .AddInc(TotalModValue(EMod.mod_physical_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_skill_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_damage_inc));

            // 조건부 Increased
            if (IsHPMax)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_full_life));
            if (IsHPHalf)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_low_life));

            // More 수집 (개별적으로 추가!)
            calc.AddMore(TotalModValue(EMod.mod_physical_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_skill_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_damage_more));

            return calc;
        }

        /// <summary>
        /// Fire 데미지 Calculator 반환 (새로운 방식)
        /// </summary>
        public Game.Battle.ModCalculator GetFireDamageCalculator()
        {
            var calc = new Game.Battle.ModCalculator()
                // Flat 수집
                .AddFlat(TotalModValue(EMod.mod_fire_damage))
                .AddFlat(TotalModValue(EMod.mod_elemental_damage))
                .AddFlat(TotalModValue(EMod.mod_all_skill_damage))
                .AddFlat(TotalModValue(EMod.mod_all_damage))

                // Increased 수집 (합산)
                .AddInc(TotalModValue(EMod.mod_fire_damage_inc))
                .AddInc(TotalModValue(EMod.mod_elemental_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_skill_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_damage_inc));

            // 조건부 Increased
            if (IsHPMax)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_full_life));
            if (IsHPHalf)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_low_life));

            // More 수집 (개별적으로 추가!)
            calc.AddMore(TotalModValue(EMod.mod_fire_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_skill_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_damage_more));

            return calc;
        }

        /// <summary>
        /// Cold 데미지 Calculator 반환 (새로운 방식)
        /// </summary>
        public Game.Battle.ModCalculator GetColdDamageCalculator()
        {
            var calc = new Game.Battle.ModCalculator()
                // Flat 수집
                .AddFlat(TotalModValue(EMod.mod_cold_damage))
                .AddFlat(TotalModValue(EMod.mod_elemental_damage))
                .AddFlat(TotalModValue(EMod.mod_all_skill_damage))
                .AddFlat(TotalModValue(EMod.mod_all_damage))

                // Increased 수집 (합산)
                .AddInc(TotalModValue(EMod.mod_cold_damage_inc))
                .AddInc(TotalModValue(EMod.mod_elemental_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_skill_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_damage_inc));

            // 조건부 Increased
            if (IsHPMax)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_full_life));
            if (IsHPHalf)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_low_life));

            // More 수집 (개별적으로 추가!)
            calc.AddMore(TotalModValue(EMod.mod_cold_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_skill_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_damage_more));

            return calc;
        }

        /// <summary>
        /// Lightning 데미지 Calculator 반환 (새로운 방식)
        /// </summary>
        public Game.Battle.ModCalculator GetLightningDamageCalculator()
        {
            var calc = new Game.Battle.ModCalculator()
                // Flat 수집
                .AddFlat(TotalModValue(EMod.mod_lightning_damage))
                .AddFlat(TotalModValue(EMod.mod_elemental_damage))
                .AddFlat(TotalModValue(EMod.mod_all_skill_damage))
                .AddFlat(TotalModValue(EMod.mod_all_damage))

                // Increased 수집 (합산)
                .AddInc(TotalModValue(EMod.mod_lightning_damage_inc))
                .AddInc(TotalModValue(EMod.mod_elemental_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_skill_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_damage_inc));

            // 조건부 Increased
            if (IsHPMax)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_full_life));
            if (IsHPHalf)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_low_life));

            // More 수집 (개별적으로 추가!)
            calc.AddMore(TotalModValue(EMod.mod_lightning_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_skill_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_damage_more));

            return calc;
        }

        /// <summary>
        /// Poison 데미지 Calculator 반환 (새로운 방식)
        /// </summary>
        public Game.Battle.ModCalculator GetPoisonDamageCalculator()
        {
            var calc = new Game.Battle.ModCalculator()
                // Flat 수집
                .AddFlat(TotalModValue(EMod.mod_poison_damage))
                .AddFlat(TotalModValue(EMod.mod_elemental_damage))
                .AddFlat(TotalModValue(EMod.mod_all_skill_damage))
                .AddFlat(TotalModValue(EMod.mod_all_damage))

                // Increased 수집 (합산)
                .AddInc(TotalModValue(EMod.mod_poison_damage_inc))
                .AddInc(TotalModValue(EMod.mod_elemental_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_skill_damage_inc))
                .AddInc(TotalModValue(EMod.mod_all_damage_inc));

            // 조건부 Increased
            if (IsHPMax)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_full_life));
            if (IsHPHalf)
                calc.AddInc(TotalModValue(EMod.mod_damage_inc_on_low_life));

            // More 수집 (개별적으로 추가!)
            calc.AddMore(TotalModValue(EMod.mod_poison_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_skill_damage_more))
                .AddMore(TotalModValue(EMod.mod_all_damage_more));

            return calc;
        }

        // ========================================
        // 기존 메서드 (하위 호환성 유지)
        // ========================================

        public double ResultPhysicalDamage()
        {
            return ResultAllSkillDamage(
                TotalModValue(EMod.mod_physical_damage),
                TotalModValue(EMod.mod_physical_damage_inc),
                TotalModValue(EMod.mod_physical_damage_more));
        }
        public double ResultFireDamage()
        {
            var damage = TotalModValue(EMod.mod_fire_damage);
            var inc = TotalModValue(EMod.mod_fire_damage_inc);
            var more = TotalModValue(EMod.mod_fire_damage_more);
            var result = ResultElementalDamage(damage, inc, more);
            return result;
        }
        public double ResultColdDamage()
        {
            return ResultElementalDamage(
                TotalModValue(EMod.mod_cold_damage),
                TotalModValue(EMod.mod_cold_damage_inc),
                TotalModValue(EMod.mod_cold_damage_more));
        }
        public double ResultLightningDamage()
        {
            // 기본 번개 피해 inc
            double baseInc = TotalModValue(EMod.mod_lightning_damage_inc);

            // 번개 저항만큼 번개 피해 % 증가
            double lightningResistance = ResultLightningResistance();
            double incPerResistance = TotalModValue(EMod.mod_lightning_damage_inc_per_lightning_resistance);

            // 번개 저항 10%당 incPerResistance% 증가 (최대 300%)
            double bonusInc = (lightningResistance / 10.0) * incPerResistance;
            bonusInc = System.Math.Min(bonusInc, 300.0); // 최대 300% 제한

            // 최종 inc = 기본 inc + 보너스 inc
            double totalInc = baseInc + bonusInc;

            return ResultElementalDamage(
                TotalModValue(EMod.mod_lightning_damage),
                totalInc,
                TotalModValue(EMod.mod_lightning_damage_more));
        }
        public double ResultPoisonDamage()
        {
            return ResultElementalDamage(
                TotalModValue(EMod.mod_poison_damage),
                TotalModValue(EMod.mod_poison_damage_inc),
                TotalModValue(EMod.mod_poison_damage_more));
        }

        private double ResultAllSkillReinforceAdd(double InReinforce)
        {
            return GameUtility.CalcResultMod(TotalModValue(EMod.mod_use_all_skill_reinforce_add) + InReinforce);
        }
        public double ResultSkillReinforce(GameDB_Client_Skill InSkillDB, int InCoreReinforce)
        {
            return GameBattleUtilityManager.Instance.ResultSkillReinforce(GetCharacterStatus, InSkillDB, InCoreReinforce);
        }


        public double ResultSkillAoeRadius(GameDB_Client_Skill InSkillDB)
        {
            return GameBattleUtilityManager.Instance.ResultSkillAoeRadius(GetCharacterStatus, InSkillDB);
        }
        public double ResultAoeRadius()
        {
            return GameBattleUtilityManager.Instance.ResultAllAoeRadius(GetCharacterStatus, 0, 0, 0);
        }
        public double ResultKillBombAoeRadius(GameDB_Client_Skill InSkillDB)
        {
            return GameBattleUtilityManager.Instance.ResultKillBombAoeRadius(GetCharacterStatus, InSkillDB);
        }

        public bool ResultKilledBombExecute()
        {
            return GameBattleUtilityManager.Instance.ResultKilledBombExecute(GetCharacterStatus);
        }

        public double ResultSkillAdditionalTarget(double InSkillDBTarget)
        {
            return TotalModValue(EMod.mod_skill_additional_target) + InSkillDBTarget;
        }

        public int ResultSkillChain(int InSkillDBChain)
        {
            return (int)(TotalModValue(EMod.mod_skill_additional_chain) + InSkillDBChain);
        }

        public double ResultSkillChainSpeedInc()
        {
            return TotalModValue(EMod.mod_skill_additional_chain_speed_inc);
        }

        public double ResultSkillDuration(GameDB_Client_Skill InSkillDB)
        {
            return GameBattleUtilityManager.Instance.ResultSkillDuration(GetCharacterStatus, InSkillDB);
        }

        public int ResultSkillManaCost(GameDB_Client_Skill InSkillDB)
        {
            return GameBattleUtilityManager.Instance.ResultSkillManaCost(GetCharacterStatus, InSkillDB);
        }

        public double ResultSkillCastSpeed(GameDB_Client_Skill InGameDB_Client_Skill)
        {
            return GameBattleUtilityManager.Instance.ResultSkillCastSpeed(GetCharacterStatus, InGameDB_Client_Skill);
        }
        public double ResultSkillCastSpeed()
        {
            BaseSpellController mainSpell = GetCharacterActor?.GetSpellController(ESkillSlot.skill_slot_spell_1);
            if (mainSpell == null)
                return 1;

            return ResultSkillCastSpeed(mainSpell.GetSkillDBData);
        }
        public double ResultSkillCoolTime(ESkill InSkill, GameDB_Client_Skill InSkillDB)
        {
            return GameBattleUtilityManager.Instance.ResultSkillCoolTime(GetCharacterStatus, InSkill, InSkillDB);
        }

        public double ResultCriticalChance(FCharacterBaseStatus InDamageTarget)
        {
            if (TotalModValue(EMod.mod_cannot_crit) > 0)
            {
                return 0;
            }

            double addCursedCritChance = 0;
            if (InDamageTarget != null && InDamageTarget.BattleStatus != null)
            {
                // Curse 적에 대한 치명타 확률 추가
                // 대상(InDamageTarget)이 가진 cursed_enemy MOD 값 적용
                double cursedCritChance = InDamageTarget.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_crit_chance);
                double cursedCritChanceInc = InDamageTarget.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_crit_chance_inc);

                // Inc 적용: (base + cursed) * (1 + inc)
                // cursedCritChanceInc는 FLOAT_PER 타입이므로 이미 ratio 형태 (0.5 = 50%)
                addCursedCritChance = cursedCritChance * (1.0 + cursedCritChanceInc);
            }

            return GameUtility.CalcResultMod(
              TotalModValue(EMod.mod_crit_chance) + addCursedCritChance,
              TotalModValue(EMod.mod_crit_chance_inc));
        }
        public double ResultCriticalMultiplier(FCharacterBaseStatus InDamageTarget)
        {
            double addCursedCritMultiplier = 0;
            if (InDamageTarget != null && InDamageTarget.BattleStatus != null)
            {
                // Curse 적에 대한 치명타 배율 추가
                // 대상(InDamageTarget)이 가진 cursed_enemy MOD 값 적용
                addCursedCritMultiplier = InDamageTarget.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_crit_multiplier);
            }

            return GameUtility.CalcResultMod(
              TotalModValue(EMod.mod_crit_multiplier) + addCursedCritMultiplier,
              TotalModValue(EMod.mod_crit_multiplier_inc));
        }

        public double ResultCriticalBlowChance()
        {
            return GameUtility.CalcResultMod(
              TotalModValue(EMod.mod_crit_blow_chance),
              TotalModValue(EMod.mod_crit_blow_chance_inc));
        }
        public double ResultCriticalBlowmultiplier()
        {
            return GameUtility.CalcResultMod(
              TotalModValue(EMod.mod_crit_blow_multiplier),
              TotalModValue(EMod.mod_crit_blow_multiplier_inc));
        }
        /// <summary>
        /// ������ & ����
        /// </summary>
        /// <returns></returns>

        /// <summary>
        /// 최대 생명력 계산
        /// 계산식: mod_life × (1 + mod_life_inc) × (1 + mod_life_more)
        /// - mod_life: 플랫 생명력 (기본값 + 방어구 등)
        /// - mod_life_inc: 생명력 증가율 (각성, 성운 등)
        /// - mod_life_more: 생명력 곱연산 (장신구 전용)
        /// </summary>
        public double ResultMaxLife(bool InWithBuff = true)
        {
            return GameUtility.CalcResultMod(
                TotalModValue(EMod.mod_life, InWithBuff),
                TotalModValue(EMod.mod_life_inc, InWithBuff),
                TotalModValue(EMod.mod_life_more, InWithBuff));
        }
        public double ResultMaxMana(bool InWithBuff = true)
        {
            return GameUtility.CalcResultMod(
                TotalModValue(EMod.mod_mana, InWithBuff),
                TotalModValue(EMod.mod_mana_inc, InWithBuff));
        }

        /// <summary>
        /// 생명력 재생
        /// 계산식: life_regen * (1 + life_regen_inc / 100)
        /// 기본값은 PlayerDefaultMod 또는 MonsterDefaultMod의 mod_life_regen에서 설정
        /// </summary>
        /// <param name="InWithBuff">버프 포함 여부</param>
        /// <returns>초당 생명력 재생량</returns>
        public double ResultLifeRegen(bool InWithBuff = true)
        {
            // 플랫 재생량
            double flatRegen = TotalModValue(EMod.mod_life_regen, InWithBuff);

            // 재생 속도 증가
            double regenInc = TotalModValue(EMod.mod_life_regen_inc, InWithBuff);

            // 최종 재생량 = 플랫 재생 * (1 + 증가율)
            return GameUtility.CalcResultMod(flatRegen, regenInc);
        }

        /// <summary>
        /// 마나 재생
        /// 계산식: mana_regen * (1 + mana_regen_rate_inc / 100)
        /// 기본값은 PlayerDefaultMod 또는 MonsterDefaultMod의 mod_mana_regen에서 설정
        /// </summary>
        /// <param name="InWithBuff">버프 포함 여부</param>
        /// <returns>초당 마나 재생량</returns>
        public double ResultManaRegen(bool InWithBuff = true)
        {
            // 플랫 재생량
            double flatRegen = TotalModValue(EMod.mod_mana_regen, InWithBuff);

            // 재생 속도 증가
            double regenInc = TotalModValue(EMod.mod_mana_regen_rate_inc, InWithBuff);

            // 최종 재생량 = 플랫 재생 * (1 + 증가율)
            return GameUtility.CalcResultMod(flatRegen, regenInc);
        }
        /// <summary>
        /// 블록, 회피
        /// </summary>
        public double ResultBlockChange()
        {
            return GameUtility.CalcResultMod(
                TotalModValue(EMod.mod_block_chance),
                TotalModValue(EMod.mod_block_chance_inc));
        }
        public double ResultEvadeChange()
        {
            return GameUtility.CalcResultMod(
                TotalModValue(EMod.mod_evade_chance),
                TotalModValue(EMod.mod_evade_chance_inc));
        }
        //기절 회복속도
        public double ResultStunRecovery()
        {
            return GameUtility.CalcResultMod(
                TotalModValue(EMod.mod_stun_recovery_inc));
        }

        /// <summary>
        /// Chill 효과 증가 (적에게 적용)
        /// mod_chill_effect_inc_on_enemy: Chill 효과 증가
        /// 기본 효과 20%, 최대 50%
        /// </summary>
        public double ResultChillEffectOnEnemy()
        {
            // 기본 효과 20%
            double baseEffect = CHILL_BASE_EFFECT;
            double effectInc = TotalModValue(EMod.mod_chill_effect_inc_on_enemy);

            // 최종 효과 = 기본 * (1 + inc)
            double finalEffect = baseEffect * (1.0 + effectInc);

            // 최대 50%로 제한
            return Math.Min(finalEffect, CHILL_MAX_EFFECT);
        }

        /// <summary>
        /// 저항 최대치 계산
        /// 기본값 75% (GameGlobalConfig.maxResistance) + mod_*_resistance_max
        /// </summary>
        private void CalculateResistanceMax()
        {
            // 기본 저항 최대치 (75%, GameGlobalConfig.maxResistance)
            double baseMax = GameGlobalConfig.maxResistance * 100.0; // 0.75 → 75%

            // 화염 저항 최대치
            fireResistanceMax = baseMax + TotalModValue(EMod.mod_fire_resistance_max);

            // 냉기 저항 최대치
            coldResistanceMax = baseMax + TotalModValue(EMod.mod_cold_resistance_max);

            // 번개 저항 최대치
            lightningResistanceMax = baseMax + TotalModValue(EMod.mod_lightning_resistance_max);

            // 독 저항 최대치
            poisonResistanceMax = baseMax + TotalModValue(EMod.mod_poison_resistance_max);

            // 물리 저항 최대치
            physicalResistanceMax = baseMax + TotalModValue(EMod.mod_physical_resistance_max);

            // 원소 저항 최대치 (모든 원소 저항에 적용)
            double elementalResistanceMax = TotalModValue(EMod.mod_elemental_resistance_max);
            if (elementalResistanceMax != 0)
            {
                fireResistanceMax += elementalResistanceMax;
                coldResistanceMax += elementalResistanceMax;
                lightningResistanceMax += elementalResistanceMax;
                poisonResistanceMax += elementalResistanceMax;
            }
        }

        /// <summary>
        /// 최종 저항 계산 (공격자의 관통, % 감소 적용)
        ///
        /// 저항 최대치 작동 방식:
        /// - 기본 최대치: 75% (GameGlobalConfig.maxResistance)
        /// - 저항 최대치 MOD: mod_*_resistance_max로 최대치 증가 가능
        /// - 예: 저항 합계 0.8 (80%), 기본 최대치 0.75 (75%) → 0.75만 적용
        /// - 예: 저항 합계 0.8 (80%), 최대치 0.85 (85%) → 0.8 전부 적용
        /// - 예: 저항 합계 0.9 (90%), 최대치 0.85 (85%) → 0.85만 적용 (최대치 클램핑)
        /// </summary>
        /// <param name="baseResistance">방어자의 기본 저항 (합산값, ratio: 0.75 = 75%)</param>
        /// <param name="resistanceMax">저항 최대치 (ratio: 0.75 = 75%)</param>
        /// <param name="attackerPenetration">공격자의 저항 관통 (ratio: 0.2 = 20%)</param>
        /// <param name="attackerReduction">공격자의 적 저항 % 감소 (ratio: 0.3 = 30%)</param>
        /// <returns>최종 저항 (-1.0 ~ 최대치, ratio)</returns>
        public static double CalculateFinalResistance(
            double baseResistance,
            double resistanceMax,
            double attackerPenetration,
            double attackerReduction)
        {
            // 1단계: 저항 최대치 적용 (저항 합계가 최대치를 초과하면 최대치로 제한)
            double clampedResistance = System.Math.Min(baseResistance, resistanceMax);

            // 2단계: 공격자의 적 저항 % 감소 적용
            // attackerReduction은 FLOAT_PER이므로 이미 ratio 형태 (0.3 = 30%)
            double reducedResistance = clampedResistance * (1.0 - attackerReduction);

            // 3단계: 공격자의 저항 관통 차감
            double effectiveResistance = reducedResistance - attackerPenetration;

            // 4단계: 최종 저항 클램핑 (-100% ~ 최대치)
            // ratio 형태로 -1.0 = -100%, resistanceMax는 이미 ratio
            double finalResistance = System.Math.Clamp(effectiveResistance, -1.0, resistanceMax);

            return finalResistance;
        }

        /// <summary>
        /// ����
        /// </summary>
        private double ResultElementalResistance(double InAddRes, double InAddResMax)
        {
            double value = TotalModValue(EMod.mod_elemental_resistance) + InAddRes;
            double valueMax = TotalModValue(EMod.mod_elemental_resistance_max) + InAddResMax;

            if (value > valueMax)
            {
                value = valueMax;
            }

            return value;
        }
        private double ResultElementalResistancePenetration(double InAddValue)
        {
            return TotalModValue(EMod.mod_elemental_resistance_penetration) + InAddValue;
        }

        public double ResultColdResistance()
        {
            return ResultElementalResistance(
                TotalModValue(EMod.mod_cold_resistance),
                TotalModValue(EMod.mod_cold_resistance_max));
        }
        public double ResultColdResistanceMax()
        {
            return coldResistanceMax;
        }
        public double ResultColdResistancePenetration()
        {
            return ResultElementalResistancePenetration(
                TotalModValue(EMod.mod_cold_resistance_penetration));
        }
        public double ResultFireResistance()
        {
            return ResultElementalResistance(
            TotalModValue(EMod.mod_fire_resistance),
            TotalModValue(EMod.mod_fire_resistance_max));
        }
        public double ResultFireResistanceMax()
        {
            return fireResistanceMax;
        }
        public double ResultFireResistancePenetration()
        {
            return ResultElementalResistancePenetration(
         TotalModValue(EMod.mod_fire_resistance_penetration));
        }
        public double ResultLightningResistance()
        {
            return ResultElementalResistance(
            TotalModValue(EMod.mod_lightning_resistance),
            TotalModValue(EMod.mod_lightning_resistance_max));
        }
        public double ResultLightningResistanceMax()
        {
            return lightningResistanceMax;
        }
        public double ResultLightningResistancePenetration()
        {
            return ResultElementalResistancePenetration(
         TotalModValue(EMod.mod_lightning_resistance_penetration));
        }
        public double ResultPoisonResistance()
        {
            return ResultElementalResistance(
            TotalModValue(EMod.mod_poison_resistance),
            TotalModValue(EMod.mod_poison_resistance_max));
        }
        public double ResultPoisonResistanceMax()
        {
            return poisonResistanceMax;
        }
        public double ResultPoisonResistancePenetration()
        {
            return ResultElementalResistancePenetration(
         TotalModValue(EMod.mod_poison_resistance_penetration));
        }
        /// <summary>
        /// 물리 피해 감소
        /// </summary>
        public double ResultPhysicalDamageReduction()
        {
            return TotalModValue(EMod.mod_physical_damage_reduction);
        }

        /// <summary>
        /// 물리 저항 (방어력 전환 - 원소 저항과 동일한 방식)
        /// </summary>
        public double ResultPhysicalResistance()
        {
            double baseResistance = TotalModValue(EMod.mod_physical_resistance);
            // 물리 저항은 원소 저항과 달리 elemental 보너스가 적용되지 않음
            return System.Math.Min(baseResistance, physicalResistanceMax);
        }

        /// <summary>
        /// 물리 저항 최대치
        /// </summary>
        public double ResultPhysicalResistanceMax()
        {
            return physicalResistanceMax;
        }

        /// <summary>
        /// 물리 저항 관통
        /// </summary>
        public double ResultPhysicalResistancePenetration()
        {
            return TotalModValue(EMod.mod_physical_resistance_penetration);
        }

        /// <summary>
        /// 적 물리 저항 감소
        /// </summary>
        public double ResultReductionEnemyPhysicalResistance()
        {
            return TotalModValue(EMod.mod_reduction_enemy_physical_resistance);
        }

        private double ResultEnemyElementalResistanceResistance(double InAddValue)
        {
            return TotalModValue(EMod.mod_reduction_enemy_elemental_resistance) + InAddValue;
        }
        public double ResultReductionEnemyColdResistance()
        {
            return ResultEnemyElementalResistanceResistance(TotalModValue(EMod.mod_reduction_enemy_cold_resistance));
        }
        public double ResultReductionEnemyFireResistance()
        {
            return ResultEnemyElementalResistanceResistance(TotalModValue(EMod.mod_reduction_enemy_fire_resistance));
        }
        public double ResultReductionEnemyLightningResistance()
        {
            return ResultEnemyElementalResistanceResistance(TotalModValue(EMod.mod_reduction_enemy_lightning_resistance));
        }
        public double ResultReductionEnemyPoisonResistance()
        {
            return ResultEnemyElementalResistanceResistance(TotalModValue(EMod.mod_reduction_enemy_poison_resistance));
        }
        /// <summary>
        /// �鿪
        /// </summary>
        public bool ResultImmuneCheck(EMod InImmuneType)
        {
            switch (InImmuneType)
            {
                case EMod.mod_immune_arctic:
                case EMod.mod_immune_bleeding:
                case EMod.mod_immune_chill:
                case EMod.mod_immune_curse:
                case EMod.mod_immune_ignite:
                case EMod.mod_immune_paralyze:
                case EMod.mod_immune_poisoning:
                case EMod.mod_immune_shock:
                case EMod.mod_immune_stun:
                    {
                        return TotalModValue(InImmuneType) > 0 ? true : false;
                    }
                default:
                    {
                        Debug.LogError($"ResultImmuneCheck Invalid Mod Immune Type = {InImmuneType}");
                        return false;
                    }
            }
        }
        /// <summary>
        /// �⺻ �̵��ӵ��� ����?
        /// </summary>
        public double ResultMovementSpeed()
        {
            double moveSpeed = TotalModValue(EMod.mod_movementspeed);
            double moveSpeedInc = TotalModValue(EMod.mod_movementspeed_inc);

            if (IsHPMax)
            {
                moveSpeedInc += TotalModValue(EMod.mod_movementspeed_inc_on_full_life);
            }
            else if (IsHPHalf)
            {
                moveSpeedInc += TotalModValue(EMod.mod_movementspeed_inc_on_low_life);
            }

            // Ailment CC 효과 체크 (우선순위: Stun > Paralyze > Chill)
            FCharacterStatus charStatus = GetCharacterStatus;
            if (charStatus != null && charStatus.CharacterActor != null && charStatus.CharacterActor.BuffControl != null)
            {
                // Stun 효과: 행동 차단 (속도 = 0)
                int stunCount = charStatus.CharacterActor.BuffControl.GetBuffCount(EStatusEffect.ailment_stun);
                if (stunCount > 0)
                {
                    // Stun은 모든 행동을 차단 (이동 불가)
                    return 0.0;
                }

                // Paralyze 효과: 동작 정지 (속도 = 0)
                int paralyzeCount = charStatus.CharacterActor.BuffControl.GetBuffCount(EStatusEffect.ailment_paralyze);
                if (paralyzeCount > 0)
                {
                    // Paralyze는 동작 속도를 0으로 만듦
                    return 0.0;
                }

                // Chill 효과: 동작 속도 감소
                int chillCount = charStatus.CharacterActor.BuffControl.GetBuffCount(EStatusEffect.ailment_chill);
                if (chillCount > 0)
                {
                    // 기본 효과 20% * (1 + mod_chill_effect_inc_on_enemy)
                    double chillEffect = ResultChillEffectOnEnemy();
                    double chillEffectPercent = Math.Min(chillEffect, CHILL_MAX_EFFECT);

                    // 동작 속도 감소 (음수 적용)
                    moveSpeedInc -= chillEffectPercent * 100.0; // 퍼센트 값으로 변환
                }

                // Curse 행동속도 감소: mod_cursed_enemy_action_speed_red
                // 이 MOD는 debuff_confusion과 같은 Curse 디버프를 통해 적용됨
                double cursedActionSpeedRed = TotalModValue(EMod.mod_cursed_enemy_action_speed_red);
                if (cursedActionSpeedRed != 0)
                {
                    moveSpeedInc += cursedActionSpeedRed; // 음수 값으로 속도 감소
                }
            }

            return GameUtility.CalcResultMod(moveSpeed, moveSpeedInc);
        }

        public double ResultLifeLeechDamage(UCharacterActor InDamagedCharacter, FSkillData InSkillData, double InDamage)
        {
            return CalcResultLifeLeechDamage(InDamagedCharacter, EStatusEffect.leech_life, InSkillData, InDamage);
        }
        public double ResultManaLeechDamage(UCharacterActor InDamagedCharacter, FSkillData InSkillData, double InDamage)
        {
            return CalcResultLifeLeechDamage(InDamagedCharacter, EStatusEffect.leech_mana, InSkillData, InDamage);
        }

        double CalcResultLifeLeechDamage(UCharacterActor InDamagedCharacter, EStatusEffect InLeechType, FSkillData InSkillData, double InDamage)
        {
            double InLeechDamage = 0;

            if (InLeechType == EStatusEffect.leech_life || InLeechType == EStatusEffect.leech_mana)
            {
                if (InDamagedCharacter == null)
                    return InLeechDamage;

                double addedLeechRate = 0;
                if (InLeechType == EStatusEffect.leech_life)
                {
                    if (TotalModValue(EMod.mod_cannotbe_lifeleeched) > 0)
                    {
                        return InLeechDamage;
                    }

                    addedLeechRate += TotalModValue(EMod.mod_life_leech_all_damage);

                    switch (InSkillData.SkillTagDamageType)
                    {
                        case ESkillTag.skilltag_cold: { addedLeechRate += TotalModValue(EMod.mod_life_leech_cold_damage); } break;
                        case ESkillTag.skilltag_fire: { addedLeechRate += TotalModValue(EMod.mod_life_leech_fire_damage); } break;
                        case ESkillTag.skilltag_lightning: { addedLeechRate += TotalModValue(EMod.mod_life_leech_lightning_damage); } break;
                        case ESkillTag.skilltag_poison: { addedLeechRate += TotalModValue(EMod.mod_life_leech_poison_damage); } break;
                        case ESkillTag.skilltag_physical: { addedLeechRate += TotalModValue(EMod.mod_life_leech_physical_damage); } break;
                    }
                }
                else if (InLeechType == EStatusEffect.leech_mana)
                {
                    if (TotalModValue(EMod.mod_cannotbe_manaleeched) > 0)
                    {
                        return InLeechDamage;
                    }

                    addedLeechRate += TotalModValue(EMod.mod_mana_leech_all_damage);

                    switch (InSkillData.SkillTagDamageType)
                    {
                        case ESkillTag.skilltag_cold: { addedLeechRate += TotalModValue(EMod.mod_mana_leech_cold_damage); } break;
                        case ESkillTag.skilltag_fire: { addedLeechRate += TotalModValue(EMod.mod_mana_leech_fire_damage); } break;
                        case ESkillTag.skilltag_lightning: { addedLeechRate += TotalModValue(EMod.mod_mana_leech_lightning_damage); } break;
                        case ESkillTag.skilltag_poison: { addedLeechRate += TotalModValue(EMod.mod_mana_leech_poison_damage); } break;
                        case ESkillTag.skilltag_physical: { addedLeechRate += TotalModValue(EMod.mod_mana_leech_physical_damage); } break;
                    }
                }

                InLeechDamage = (InDamage * addedLeechRate);
            }

            return InLeechDamage;
        }

        /// <summary>
        /// min MOD가 있는 버프는 버프가 없어도 최소 스택 MOD 자동 적용 (시뮬레이터 로직과 동일)
        /// mod_buff_xxx_min이 있으면 항상 최소 개수의 MOD를 buffData에 적용
        /// </summary>
        private void AutoApplyMinBuffModifiers(FCharacterSingleData InBasicStatus)
        {
            // 플레이어만 처리 (몬스터는 버프 없음)
            if (InBasicStatus.CharacterSingleDataType != ECharacterSingleDataType.Player)
                return;

            // GameDB에서 버프 정보 가져오기
            var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff;
            if (buffDB == null || buffDB.MapData == null)
                return;

            // 각 버프별로 min MOD 체크
            EStatusEffect[] buffsToCheck = new EStatusEffect[]
            {
                EStatusEffect.buff_killingspree,
                EStatusEffect.buff_backstab,
                EStatusEffect.buff_ignorepain,
                EStatusEffect.buff_frenzy
            };

            foreach (var buffType in buffsToCheck)
            {
                // min MOD 가져오기
                EMod minStackMod = GetBuffMinStackMod(buffType);
                if (minStackMod == EMod.None)
                    continue;

                // min MOD 값 확인
                double minModValue = TotalModValue(minStackMod);
                if (minModValue <= 0)
                    continue;

                int minStack = (int)minModValue;

                // 현재 버프 개수 확인
                UCharacterActor characterActor = GetCharacterActor;
                if (characterActor == null || characterActor.BuffControl == null)
                    continue;

                int currentStack = characterActor.BuffControl.GetBuffCount(buffType);

                // 현재 스택이 최소값보다 적으면 최소값으로 MOD 적용
                if (currentStack < minStack)
                {
                    // GameDB에서 버프 데이터 읽기
                    if (!buffDB.MapData.TryGetValue(buffType, out GameDB_Client_Buff buffData))
                        continue;

                    // BuffMod가 정의되지 않은 경우 스킵
                    if (buffData.BuffMod == null || buffData.BuffMod.Count == 0)
                        continue;

                    int per = buffData.Per.Value;
                    EStatusEffectTag tag = buffData.Tag;

                    // Effect/EffectMax
                    double effectMultiplier = (double)buffData.Effect.Value;
                    if (effectMultiplier == 0)
                        effectMultiplier = 1.0;

                    double effectMax = (double)buffData.EffectMax.Value;

                    // 각 BuffMod마다 개별 계산 (최소 스택으로)
                    for (int i = 0; i < buffData.BuffMod.Count; i++)
                    {
                        var modData = buffData.BuffMod[i];
                        double baseModValue = modData.Value.GetValue;
                        double finalValue = 0;

                        // Tag에 따른 계산
                        switch (tag)
                        {
                            case EStatusEffectTag.bufftag_charge:
                            case EStatusEffectTag.bufftag_stack:
                                // (최소수량 / Per) * effectMultiplier * baseModValue
                                finalValue = (minStack / (double)per) * effectMultiplier * baseModValue;
                                break;

                            case EStatusEffectTag.bufftag_highest:
                                // Stack은 항상 1, 가장 강한 효과만
                                finalValue = (1.0 / per) * effectMultiplier * baseModValue;
                                break;

                            default:
                                finalValue = baseModValue;
                                break;
                        }

                        // EffectMax 제한
                        if (effectMax > 0)
                        {
                            finalValue = UnityEngine.Mathf.Min((float)finalValue, (float)effectMax);
                        }

                        // BattleStatus의 buffData에 적용
                        this.buffData.AddModValue(modData.Mod, CryptoValueDouble.Create(finalValue));
                    }
                }
            }
        }

        /// <summary>
        /// 버프의 최소 스택 MOD 가져오기 (시뮬레이터 로직과 동일)
        /// </summary>
        private EMod GetBuffMinStackMod(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return EMod.mod_buff_killingspree_min;
                case EStatusEffect.buff_backstab:
                    return EMod.mod_buff_backstab_min;
                case EStatusEffect.buff_ignorepain:
                    return EMod.mod_buff_ignorepain_min;
                case EStatusEffect.buff_frenzy:
                    return EMod.mod_buff_frenzy_min;
                default:
                    return EMod.None;
            }
        }
    }

}