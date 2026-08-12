Shader "PX/MonsterToonUnlitURP"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)

        // 기존 쉐이더 호환성을 위한 별칭
        [HideInInspector] _MainTex("Main Texture (Legacy)", 2D) = "white" {}
        [HideInInspector] _Color("Color (Legacy)", Color) = (1,1,1,1)

        [Space(6)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2.0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0.0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        // ===================================================================
        // 적용 순서: Base → Volume Shading → Rim → Emission
        // ===================================================================

        [Header(Volume Shading (Directional))]
        [Toggle(_HEIGHT_SHADING)] _UseHeightShading("Use Volume Shading", Float) = 0
        // 음영 축 공간. World = _ShadingDir 를 월드 방향으로 사용(기존 동작),
        // MainLight = 씬의 메인 디렉셔널 라이트 방향을 따라감,
        // View = _ShadingDir 를 카메라 기준으로 해석해 어느 각도에서 봐도 같은 면이 밝다.
        [KeywordEnum(World, MainLight, View)] _ShadeSpace("Shading Space", Float) = 0
        // 광원 방향(표면에서 광원을 향하는 벡터). (0,1,0) 이면 기존 Y축 그라디언트와 완전히 동일.
        _ShadingDir("Shading Direction (XYZ)", Vector) = (0, 1, 0, 0)
        _TopTint("Lit Tint (facing light)", Color) = (1.08, 1.05, 1.0, 1)
        _BottomTint("Unlit Tint (facing away)", Color) = (0.42, 0.45, 0.56, 1)
        // N dot L 이 Min일 때 BottomTint, Max일 때 TopTint. 범위를 좁히면 대비가 강해진다.
        // 기본값 -1 ~ 1 은 (d+1)/2 즉 Half-Lambert 와 같은 식이라 뒷면도 완전히 검어지지 않는다.
        _HeightRangeMin("Shade Range Min (dark side)", Range(-1, 1)) = -1.0
        _HeightRangeMax("Shade Range Max (lit side)", Range(-1, 1)) = 1.0

        [Header(Rim Light)]
        [Toggle(_RIM_LIGHT)] _UseRimLight("Use Rim Light", Float) = 0
        [HDR] _RimColor("Rim Color", Color) = (0.88, 0.92, 1.0, 1)
        _RimIntensity("Rim Intensity", Range(0, 2)) = 0.1
        _RimPower("Rim Power (narrower as higher)", Range(0.1, 10)) = 4.0
        // Threshold - Smoothness 가 0 이하면 정면 픽셀까지 림이 깔린다. 주의.
        _RimThreshold("Rim Threshold", Range(0, 1)) = 0.65
        _RimSmoothness("Rim Smoothness (softness)", Range(0.001, 0.5)) = 0.25

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("Use Emission", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
        _EmissionMap("Emission Map", 2D) = "white" {}
        _EmissionIntensity("Emission Intensity", Range(0, 8)) = 1.0

        [Header(Hit Flash)]
        // 피격 순간 몸 전체를 잠깐 단색으로 덮는다. 값은 런타임에 UCharacterActor 가
        // MaterialPropertyBlock 으로 개체별로 넣으므로, 여기 값은 머티리얼 미리보기용 기본값일 뿐이다.
        // Blend 는 반드시 0 으로 두어야 한다 — 1 로 저장된 머티리얼이 섞이면 그 몬스터는 상시 하얗게 나온다.
        [HDR] _HitFlashColor("Hit Flash Color", Color) = (1, 1, 1, 1)
        _HitFlashBlend("Hit Flash Blend", Range(0, 1)) = 0.0

        [Header(Particle)]
        _SoftParticlesNearFadeDistance("Soft Particles Near Fade", Float) = 0.0
        _SoftParticlesFarFadeDistance("Soft Particles Far Fade", Float) = 1.0

        // -------------------------------------
        // Hidden properties
        // Surface / Blend 는 URP ShaderGUI 가 관리하던 값. CustomEditor 를 제거했으므로
        // 머티리얼에 저장된 기존 값이 그대로 유지된다 (렌더링 동작 불변).
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__mode", Float) = 0.0
        [HideInInspector] _BlendOp("__blendop", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0

        [HideInInspector] _ColorMode("_ColorMode", Float) = 0.0
        [HideInInspector] _BaseColorAddSubDiff("_ColorMode", Vector) = (0,0,0,0)
        [HideInInspector] _FlipbookBlending("__flipbookblending", Float) = 0.0
        [HideInInspector] _SoftParticlesEnabled("__softparticlesenabled", Float) = 0.0
        [HideInInspector] _SoftParticleFadeParams("__softparticlefadeparams", Vector) = (0,0,0,0)

        [HideInInspector] _QueueOffset("Queue offset", Float) = 0.0
    }

    HLSLINCLUDE
    #pragma never_use_dxc
    
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // SRP Batcher 호환: float4 를 먼저, float 를 뒤에 모아 패킹 낭비를 줄인다
    //
    // ⚠️ _HitFlashColor / _HitFlashBlend 는 런타임에 MaterialPropertyBlock 으로 개체별로 덮어쓴다.
    // 현재 파이프라인은 SRP Batcher 가 꺼져 있고 대상이 SkinnedMeshRenderer(원래 배칭 불가)라
    // MPB 로 인한 배칭 손실이 없다. SRP Batcher 를 켜게 되면 MPB 를 쓰는 렌더러는 배처에서 이탈하므로
    // 그때는 이 두 값을 인스턴싱 프로퍼티(UNITY_DOTS_INSTANCING / per-instance CBUFFER)로 옮겨야 한다
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float4 _EmissionColor;
        float4 _RimColor;
        float4 _TopTint;
        float4 _BottomTint;
        float4 _ShadingDir;
        float4 _HitFlashColor;
        float _Cutoff;
        float _HitFlashBlend;
        float _HeightRangeMin;
        float _HeightRangeMax;
        float _RimIntensity;
        float _RimPower;
        float _RimThreshold;
        float _RimSmoothness;
        float _EmissionIntensity;
        float _SoftParticlesNearFadeDistance;
        float _SoftParticlesFarFadeDistance;
    CBUFFER_END

    TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
    TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);
    
    #ifdef _SOFTPARTICLES_ON
    TEXTURE2D(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);
    #endif

    struct Attributes
    {
        float4 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float2 uv           : TEXCOORD0;
        float4 color        : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS   : SV_POSITION;
        float2 uv           : TEXCOORD0;
        // 노멀은 Volume Shading / Rim 이 켜졌을 때만 필요하다
        #if defined(_RIM_LIGHT) || defined(_HEIGHT_SHADING)
        float3 normalWS     : TEXCOORD1;
        #endif
        // 뷰 방향은 Rim 전용 — 대부분의 머티리얼은 Rim 이 꺼져 있어 보간기를 낭비하지 않는다
        #ifdef _RIM_LIGHT
        float3 viewDirWS    : TEXCOORD2;
        #endif
        float4 color        : COLOR;
        #ifdef _SOFTPARTICLES_ON
        float4 screenPos    : TEXCOORD3;
        #endif
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // Volume Shading 이 사용하는 광원 축(표면 → 광원 방향)
    //   World     : _ShadingDir 을 월드 방향 그대로 사용
    //   MainLight : 씬의 메인 디렉셔널 라이트 방향을 따라감
    //   View      : _ShadingDir 을 카메라 기준으로 해석 → 카메라가 돌아도 같은 면이 밝다
    half3 GetShadingDirection()
    {
        // 0 벡터가 들어오면 normalize 가 NaN 이 되므로 Y축으로 폴백
        half3 dirRaw = _ShadingDir.xyz;
        dirRaw = dot(dirRaw, dirRaw) > 1e-6 ? dirRaw : half3(0, 1, 0);

        #if defined(_SHADESPACE_MAINLIGHT)
            // Lighting.hlsl 없이 Input.hlsl 의 전역 변수를 직접 읽는다
            return normalize(_MainLightPosition.xyz);
        #elif defined(_SHADESPACE_VIEW)
            // 변환 후 한 번만 정규화하면 된다 (변환 전 normalize 는 중복이었다)
            return normalize(mul((float3x3)UNITY_MATRIX_I_V, dirRaw));
        #else
            return normalize(dirRaw);
        #endif
    }

    // 림 라이트 (라이트 불필요, 뷰 방향만 사용)
    // _RimSmoothness 를 키우면 하드 엣지 → 부드러운 프레넬로 바뀐다
    half3 CalculateUnlitRim(half3 normalWS, half3 viewDir)
    {
        half NdotV = saturate(dot(normalWS, viewDir));
        half rim = 1.0 - NdotV;
        rim = pow(rim, _RimPower);

        rim = smoothstep(_RimThreshold - _RimSmoothness,
                        _RimThreshold + _RimSmoothness,
                        rim);

        return rim * _RimColor.rgb * _RimIntensity;
    }

    // Soft Particle 페이드 (Near ~ Far 구간에서 서서히 나타남)
    half CalculateSoftParticleFade(float4 screenPos)
    {
        #ifdef _SOFTPARTICLES_ON
        float2 screenUV = screenPos.xy / screenPos.w;
        float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV), _ZBufferParams);
        float thisZ = LinearEyeDepth(screenPos.z / screenPos.w, _ZBufferParams);
        float fadeRange = max(_SoftParticlesFarFadeDistance - _SoftParticlesNearFadeDistance, 0.0001);
        half fade = saturate((sceneZ - thisZ - _SoftParticlesNearFadeDistance) / fadeRange);
        return fade;
        #else
        return 1.0;
        #endif
    }

    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // ------------------------------------------------------------------
        // Main Unlit Toon Pass
        Pass
        {
            Name "UnlitToon"
            Tags { "LightMode" = "UniversalForward" }

            BlendOp[_BlendOp]
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex UnlitToonVertex
            #pragma fragment UnlitToonFragment

            // Keywords
            #pragma shader_feature_local _RIM_LIGHT
            #pragma shader_feature_local _HEIGHT_SHADING
            #pragma shader_feature_local _SHADESPACE_WORLD _SHADESPACE_MAINLIGHT _SHADESPACE_VIEW
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local _SOFTPARTICLES_ON
            #pragma shader_feature_local _FLIPBOOKBLENDING_ON

            // 프로젝트 전 씬에서 Fog 를 쓰지 않아 multi_compile_fog 를 제거했다.
            // 포그를 켜려면 이 pragma 와 fogFactor varying, MixFog 호출을 되살려야 한다.
            #pragma multi_compile_instancing

            Varyings UnlitToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;

                #if defined(_RIM_LIGHT) || defined(_HEIGHT_SHADING)
                output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
                #endif
                #ifdef _RIM_LIGHT
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                #endif

                #ifdef _SOFTPARTICLES_ON
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                #endif

                return output;
            }

            half4 UnlitToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Base Color (텍스처 + 버텍스 컬러)
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 baseColor = baseMap * _BaseColor * input.color;

                #ifdef _ALPHATEST_ON
                clip(baseColor.a - _Cutoff);
                #endif

                // 최종 색상 시작
                half3 finalColor = baseColor.rgb;

                #if defined(_RIM_LIGHT) || defined(_HEIGHT_SHADING)
                half3 normalWS = normalize(input.normalWS);
                #endif
                #ifdef _RIM_LIGHT
                half3 viewDirWS = normalize(input.viewDirWS);
                #endif
                #ifdef _HEIGHT_SHADING
                half3 shadeDir = GetShadingDirection();
                #endif

                // ===== Volume Shading (방향성 음영, 텍스처 불필요) =====
                // 광원 축을 향한 면은 _TopTint, 등진 면은 _BottomTint 로 물든다.
                // _ShadingDir 이 (0,1,0) 이면 노멀 Y 그라디언트와 수학적으로 완전히 동일하다.
                #ifdef _HEIGHT_SHADING
                half heightRange = max(_HeightRangeMax - _HeightRangeMin, 0.001);
                half ndl = dot(normalWS, shadeDir);
                half upDot = saturate((ndl - _HeightRangeMin) / heightRange);
                // 음영 세기는 Top/Bottom Tint 와 Range 로 조절한다.
                // 예전엔 pow(Power) 와 lerp(Strength) 가 더 있었으나 두 값 모두 Range/Tint 로
                // 대체 가능한 중복 파라미터였고, 실사용 머티리얼이 전부 1.0 이라 픽셀당 낭비였다.
                finalColor *= lerp(_BottomTint.rgb, _TopTint.rgb, upDot);
                #endif

                // ===== Rim Light (뷰 방향 기반, 라이트 불필요) =====
                #ifdef _RIM_LIGHT
                half3 rimLight = CalculateUnlitRim(normalWS, viewDirWS);
                finalColor += rimLight;
                #endif

                // ===== Emission =====
                #ifdef _EMISSION
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb * _EmissionIntensity;
                finalColor += emission;
                #endif

                // ===== Hit Flash =====
                // 피격 순간 몸 전체를 단색으로 덮는다. 음영·림·이미션이 다 끝난 뒤에 덮어야
                // "번쩍했다"가 몬스터 종류와 무관하게 같은 세기로 읽힌다 (앞에 넣으면 어두운 몬스터만 티가 난다).
                //
                // 셰이더 키워드로 분기하지 않고 항상 lerp 한다. blend 가 0 이면 결과가 원래 색과 동일하고,
                // 픽셀당 lerp 1회는 측정에 잡히지 않는 반면, 키워드를 쓰면 변형(variant)이 2배로 늘고
                // 개체마다 켜짐/꺼짐이 갈려 배치가 쪼개진다.
                finalColor = lerp(finalColor, _HitFlashColor.rgb, saturate(_HitFlashBlend));

                // Soft Particles
                half particleFade = 1.0;
                #ifdef _SOFTPARTICLES_ON
                particleFade = CalculateSoftParticleFade(input.screenPos);
                #endif
                
                half alpha = baseColor.a * particleFade;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth Only Pass (간소화)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVertex(Attributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }

    // CustomEditor 를 지정하면 URP 기본 ShaderGUI(UnlitShader)가 표준 프로퍼티만 그리고
    // 위에 정의한 커스텀 프로퍼티(Rim / Height / Shade 등)를 전부 숨긴다. 그래서 제거함.
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}