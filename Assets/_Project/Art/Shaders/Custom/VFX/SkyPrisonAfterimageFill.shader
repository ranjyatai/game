// 复制自 Spine/Skeleton Fill（Assets/Spine/Runtime/spine-unity/Shaders/Spine-Skeleton-Fill.shader），
// 改了三处：ZTest Always（原来是默认的 ZTest LEqual）、_Additive 加算开关、部件重叠去重。
//
// 为什么需要单独一份，不能直接用原版：闪避/突刺残影是角色某一帧姿势在世界空间的
// 静态快照，会跟场景里的墙体/装饰物发生真实的 3D 深度关系——原版 Shader 走标准
// 深度测试，贴墙闪避时残影处在墙体"后面"的那部分会被正常裁掉，表现为"半个身子
// 穿墙、缺一块"。角色本体不会有这个问题，是因为它用的是另一套遮挡合成 Shader，
// 靠落地深度图逐像素判定"显示成半透明全息"而不是被真实 3D 几何物理裁切——残影
// 不需要那一整套遮挡系统，最简单的等效做法就是干脆关掉深度测试，让残影永远完整
// 显示在最上层，跟角色贴墙时"整体保留、只是变淡"的观感保持一致。
Shader "SkyPrison/AfterimageFill" {
	Properties {
		_FillColor ("FillColor", Color) = (1,1,1,1)
		_FillPhase ("FillPhase", Range(0, 1)) = 0
		_Additive ("Additive (0 = 普通半透明, 1 = 加算)", Range(0, 1)) = 0
		[NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
		[Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput("Straight Alpha Texture", Int) = 1
		// 部件重叠去重用，由 SkyPrisonAfterimageEmitter 写入，不要手调。
		[HideInInspector] _GhostFade ("Ghost Fade", Float) = 1
		[HideInInspector] _GhostChannel ("Ghost Coverage Channel", Vector) = (1,0,0,0)
	}
	SubShader {
		Tags { "Queue"="Transparent+10" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
		Blend One OneMinusSrcAlpha
		Cull Off
		ZWrite Off
		ZTest Always
		Lighting Off

		CGINCLUDE
		#include "UnityCG.cginc"

		sampler2D _MainTex;
		float4 _FillColor;
		float _FillPhase;
		float _Additive;
		float _GhostFade;
		float4 _GhostChannel;

		// 部件重叠去重：残影是 Spine 一片片部件拼成的，加算时手臂压身体这类重叠处会亮一倍。
		// HologramCoverageFeature 先用下面的 GhostCoverage Pass 把每个残影「去掉淡出后的部件
		// 形状 alpha」累加进 _SP_GhostCoverage 的一个通道（每个残影按池序号占 RGBA 其中一个，
		// 相邻残影互不干扰、照常叠加），Normal Pass 每片按 min(总和,1)/总和 缩放，
		// 同一个残影内部重叠几层，加起来都正好是一层。
		sampler2D _SP_GhostCoverage;
		float _SP_GhostCoverageActive;

		struct VertexInput {
			float4 vertex : POSITION;
			float2 uv : TEXCOORD0;
			float4 vertexColor : COLOR;
		};

		struct VertexOutput {
			float4 pos : SV_POSITION;
			float2 uv : TEXCOORD0;
			float4 vertexColor : COLOR;
			float4 screenPos : TEXCOORD1;
		};

		VertexOutput vert (VertexInput v) {
			VertexOutput o = (VertexOutput)0;
			o.uv = v.uv;
			o.vertexColor = v.vertexColor;
			o.pos = UnityObjectToClipPos(v.vertex);
			o.screenPos = ComputeScreenPos(o.pos);
			return o;
		}
		ENDCG

		Pass {
			Name "Normal"

			CGPROGRAM
			#pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
			#pragma vertex vert
			#pragma fragment frag

			float4 frag (VertexOutput i) : SV_Target {
				float4 rawColor = tex2D(_MainTex, i.uv);
				float finalAlpha = (rawColor.a * i.vertexColor.a);

				#if defined(_STRAIGHT_ALPHA_INPUT)
				rawColor.rgb *= rawColor.a;
				#endif

				float3 finalColor = lerp((rawColor.rgb * i.vertexColor.rgb), (_FillColor.rgb * finalAlpha), _FillPhase);

				if (_SP_GhostCoverageActive > 0.5) {
					float2 suv = i.screenPos.xy / max(i.screenPos.w, 0.00001);
					float sum = dot(tex2D(_SP_GhostCoverage, suv), _GhostChannel);
					if (sum > 0.0001) {
						float w = min(sum, 1.0) / sum;
						finalColor *= w;
						finalAlpha *= w;
					}
				}

				// 混合是预乘 Alpha（One OneMinusSrcAlpha）：输出 alpha 压到 0 就等于纯加算——
				// 残影只会把底下画面提亮，不会在亮地面上压出一块暗影。
				return fixed4(finalColor, finalAlpha * (1.0 - _Additive));
			}
			ENDCG
		}

		// 去重计数 Pass：由 HologramCoverageFeature 用 DrawRenderer 显式调用，URP 不会自己调度。
		// 写的是「去掉淡出后的部件形状 alpha」——按形状去重，淡出比例在 Normal Pass 里照常乘上。
		Pass {
			Name "GhostCoverage"
			Tags { "LightMode"="SkyPrisonGhostCoverage" }
			Blend One One

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragCoverage

			float4 fragCoverage (VertexOutput i) : SV_Target {
				float a = tex2D(_MainTex, i.uv).a * i.vertexColor.a;
				clip(a - 0.001);
				return _GhostChannel * (a / max(_GhostFade, 0.004));
			}
			ENDCG
		}
	}
	Fallback Off
}
