// "Black Flame Works" 片头：纯黑虚空里点燃一团黑白火焰（白光里夹着黑色的火舌），缓慢长大，
// 最后火焰沿字的形状向两侧蔓延，烧成 BLACK FLAME WORKS。全程程序生成，由 _T（秒）驱动，逐帧可复现。
Shader "SkyPrison/Intro/BlackFlame"
{
    Properties
    {
        _MainTex ("UI 占位（RawImage 会写这个属性，避免警告）", 2D) = "white" {}
        _Mask ("Text Mask (R hard, G soft, B glow)", 2D) = "black" {}
        _T ("Time (s)", Float) = 0
        _Duration ("Duration (s)", Float) = 12
        _Aspect ("Aspect", Float) = 1.7777
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _Mask; float _T, _Duration, _Aspect;

            float h21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 h22(float2 p) { return float2(h21(p), h21(p + 17.31)); }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float a = 0.5, s = 0.0;
                [unroll] for (int i = 0; i < 5; i++) { s += a * vn(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }

            // 上飘的灰白火星 / 灰烬：大小不一、边缘柔和、带向上的拖尾，多数很暗、少数偏亮
            float ash(float2 p, float scale, float speed, float seed, float size)
            {
                float2 q = p * scale + float2(seed * 7.3 + sin(_T * 0.25 + seed) * 0.6, -_T * speed);
                float2 c = floor(q), f = frac(q) - 0.5;
                float2 r = h22(c + seed * 31.0);
                float2 off = (r - 0.5) * 0.6 + float2(sin(_T * 0.7 + r.x * 6.28) * 0.10, 0);
                float sz = size * (0.35 + 1.1 * r.x * r.x);                      // 大小差别大
                float2 d2 = (f - off) * float2(1.0, 0.5);                         // 竖向压扁 = 向上拖尾
                float k = dot(d2, d2) / (sz * sz);
                float body = exp(-k * 2.6);                                       // 高斯柔边，没有硬边
                float on = step(0.72, h21(c + 5.7 + seed));
                float bright = 0.12 + 0.88 * r.y * r.y * r.y;                     // 多数暗，少数亮
                float tw = 0.82 + 0.18 * sin(_T * (1.2 + r.y * 2.0) + r.x * 20.0);
                return body * on * bright * tw;
            }

            // 一团火焰：返回亮度（0 = 黑）。base = 火焰根部，hgt / wid = 高 / 半宽，t = 时间。
            float flame(float2 p, float2 baseP, float hgt, float wid, float t, float blackAmt)
            {
                float2 q = p - baseP;
                float hh = q.y / hgt;                                          // 0 根部 → 1 顶
                if (hh < -0.15 || hh > 1.25) return 0.0;
                float n1 = fbm(float2(q.x * 13.0, q.y * 6.0 - t * 0.55));          // 噪声按世界坐标取样，不随火焰大小缩放
                float n2 = fbm(float2(q.x * 26.0 + 7.0, q.y * 11.5 - t * 1.05));
                q.x += (n1 - 0.5) * wid * (0.5 + 1.6 * saturate(hh));          // 越往上越飘
                float prof = wid * (0.30 + 0.70 * sqrt(saturate(1.0 - hh))) * smoothstep(-0.1, 0.22, hh);
                float shape = 1.0 - abs(q.x) / max(prof, 1e-4);
                float dens = saturate(shape * 1.4 + (n2 - 0.5) * 0.9 - saturate(hh) * 0.35);
                dens *= smoothstep(1.2, 0.55, hh);                              // 顶部被撕碎
                float lum = pow(dens, 1.35) * (0.8 + 1.4 * saturate(1.0 - hh));
                // 黑色火舌：白焰里被"吃"掉的暗纹，顺着火焰向上拉长
                float bl = fbm(float2(q.x * 22.0 + 3.0, q.y * 7.5 - t * 0.8 + n1 * 1.4));
                float cut = smoothstep(0.40, 0.54, bl) * smoothstep(0.05, 0.35, dens);
                float bl2 = fbm(float2(q.x * 50.0 + 9.0, q.y * 17.5 - t * 1.3 + n2 * 1.2));   // 更细的黑色丝状火舌
                float cut2 = smoothstep(0.50, 0.62, bl2) * smoothstep(0.05, 0.3, dens);
                return lum * (1.0 - saturate(blackAmt * (0.95 * cut + 0.7 * cut2)));
            }

            static const float FloorY = -0.20;                                  // 地面接触线（火焰根部）

            // 火焰 + 字 + 光晕的亮度；p 为画面坐标，uv 为对应的纹理坐标。地面倒影会用镜像后的 p / uv 再调一次
            float scene(float2 p, float2 uv)
            {
                float T = _T;
                // ── 阶段参数 ──
                float ignite = smoothstep(0.95, 1.2, T);                        // 突然窜出来（不是慢慢变大）
                float grow = smoothstep(1.2, 6.5, T);                            // 之后只有很轻微的变化
                float y01 = (p.y - FloorY) / 0.5;
                float bT = saturate((T - 7.0) / 1.4);
                float coreRv = 0.34 * (1.0 - exp(-bT * 14.0)) + 0.05 * bT;      // 黑核：爆炸最初几帧就扩到刚好包住整个 LOGO（LOGO 半高约 0.27）
                float boom = smoothstep(6.8, 7.1, T);                           // 7.0 秒：黑炎爆炸，活火焰在此刻炸开
                float2 bc = float2(0.0, -0.02);
                float2 bd = (p - bc) * float2(0.85, 1.0); float br = length(bd);
                float2 baseP = float2(0.0, -0.20);
                float rise = pow(saturate((T - 0.95) / 5.05), 0.9);                 // 小火匀速长成大火，占满前摇；大火只留爆炸前的一小段
                float hgt = 0.03 + 0.31 * rise + 0.02 * grow;
                float wid = 0.012 + 0.085 * rise + 0.01 * grow;
                float sway = sin(T * 1.3) * 0.006 + sin(T * 2.9) * 0.003;
                baseP.x += sway;

                float col = 0.0;
                // 火种：点燃前一小粒会闪的白点
                float spark = smoothstep(0.4, 0.6, T) * (1.0 - ignite) * (0.5 + 0.5 * sin(T * 23.0) * sin(T * 11.0));
                col += spark * smoothstep(0.012, 0.0, length(p - baseP - float2(0, 0.01)));

                float f = flame(p, baseP, hgt, wid, T * 0.1, 0.8 + 0.2 * grow) * smoothstep(0.95, 1.08, T);
                f *= (1.0 + 0.9 * smoothstep(5.8, 6.95, T)) * (1.0 - boom);   // 爆炸前火焰越来越亮，随后炸开
                col += f;

                // ── 杯子 + 火焰（LOGO 图形）自下而上被点亮，再由文字升起 ──
                float2 muv = p / float2(_Aspect, 1.0) + 0.5;
                float3 mk = tex2Dlod(_Mask, float4(muv, 0, 0)).rgb;             // R 图形  G 文字  B 图形柔光
                float settle = smoothstep(8.4, 10.6, T);
                float rough = 0.80 + 0.20 * fbm(p * 70.0 + T * 0.4);
                float flick = 0.95 + 0.05 * sin(T * 9.0 + p.x * 12.0);
                float logoT = smoothstep(7.12, 7.8, T);                         // 爆炸最亮的一刻 LOGO 图形从爆心冲出来；7.05 秒之前完全不出现
                // LOGO 只在黑核范围内显现（核比 LOGO 大一圈），不会比爆炸先伸到圈外；核淡去后再完整放开
                float front = logoT * 0.85;
                front = lerp(min(front, coreRv * 1.0), front, smoothstep(0.3, 0.55, bT));
                float mReveal = (1.0 - smoothstep(front - 0.08, front, br)) * step(0.001, logoT);
                float textT = smoothstep(7.9, 9.6, T);
                float tReveal = smoothstep(abs(p.x) * 1.15 - 0.05, abs(p.x) * 1.15 + 0.10, textT * 0.85) * step(0.001, textT);   // 由中间向两侧展开
                float lickM = 0.0, lickT = 0.0;
                [unroll] for (int k = 0; k < 7; k++)
                {
                    float s = k * 0.011;
                    float nn = fbm(float2(p.x * 38.0 + k * 3.1, p.y * 12.0 - T * 1.6));
                    float3 tp = tex2Dlod(_Mask, float4(muv - float2(0.0, s * (0.6 + nn)), 0, 0)).rgb;
                    lickM += tp.r * (1.0 - k / 7.0) * (0.35 + nn); lickT += tp.g * (1.0 - k / 7.0) * (0.35 + nn);
                }
                float blackCut = smoothstep(0.58, 0.72, fbm(float2(p.x * 30.0, p.y * 9.0 - T * 0.9))) * (1.0 - settle) * smoothstep(0.6, 1.0, y01);
                float flatT = smoothstep(7.9, 9.0, T);                           // 之后 LOGO 变成纯黑白的平面图形：没有光影、没有倒影
                float markCol = lerp(mk.r * rough * flick * (0.85 + 0.15 * settle) * (0.82 + 0.18 * smoothstep(-0.1, 0.3, p.y)), mk.r, flatT);
                col += mReveal * (markCol * (1.0 - 0.7 * blackCut) + lickM * 0.30 * (1.0 - settle) * (1.0 - flatT) * (1.0 - 0.8 * blackCut));
                float textCol = lerp(mk.g * rough * flick * (0.55 + 0.45 * settle), mk.g, flatT);
                col += tReveal * (textCol * (1.0 - 0.6 * blackCut) + lickT * 0.38 * (1.0 - settle * 0.9) * (1.0 - flatT));

                // 火焰的冷白光晕（只有一点点，让火焰周围的虚空泛起一层雾）
                float gl = exp(-dot((p - baseP - float2(0, hgt * 0.4)) * float2(1.3, 0.9), (p - baseP - float2(0, hgt * 0.4)) * float2(1.3, 0.9)) * (6.0 - 3.0 * grow));
                col += gl * 0.10 * (ignite * 0.4 + grow) * (1.0 - boom) * (0.85 + 0.15 * sin(T * 6.1));
                col += mk.b * 0.16 * mReveal * (0.5 + 0.5 * settle) * (1.0 - flatT);
                // ── 黑炎爆炸：白焰冲击波向四周炸开，黑色火舌被气浪撕出来，随后散尽 ──
                if (T > 6.95 && bT < 1.0)
                {
                    float2 dir = bd / max(br, 1e-3);
                    float R = pow(bT, 0.55) * 0.62;                       // 爆炸只比 LOGO 大一圈，刚好把它包住
                    float shell = smoothstep(R - 0.28, R - 0.03, br) * (1.0 - smoothstep(R - 0.03, R + 0.03, br));
                    float nr = fbm(dir * 2.6 + float2(br * 5.0 - T * 3.5, 4.0));
                    float nr2 = fbm(dir * 5.5 + float2(br * 9.0 - T * 2.2, 9.0));
                    float cutB = smoothstep(0.42, 0.56, fbm(dir * 3.4 + float2(br * 6.0 - T * 2.8 + nr * 2.0, 2.0)));
                    float bright = shell * (0.35 + 1.1 * nr) * (1.0 - 0.92 * cutB) * (1.0 - smoothstep(0.5, 1.0, bT));
                    float flash = exp(-(T - 7.0) * 4.5) * exp(-br * 3.4);
                    // 四射的火舌：从爆心向外射出的锥形火焰，长短粗细各不相同，末端变细，带抖动，前半段快速射出
                    const float NJ = 14.0;
                    float warpA = (fbm(dir * 1.6 + float2(br * 4.5 - T * 2.5, 3.0)) - 0.5) * 2.4 * smoothstep(0.1, 0.5, br);   // 先把角度连续地扭曲，臂就是平滑弯曲的，不会被扇区切断
                    float sc = (atan2(bd.y, bd.x) + warpA) / 6.2831853 * NJ; float jid = floor(sc); float jf = frac(sc) - 0.5;
                    float rj = h21(float2(jid, 3.7)), rl = h21(float2(jid, 8.1));
                    float Lj = 0.38 + 0.5 * rl;                                 // 射程缩短                                   // 这根火舌能射多远
                    float Rj = Lj * saturate(bT * 3.2);                            // 随时间向外伸
                    float wob = (fbm(float2(br * 9.0 - T * 4.0, jid * 1.7)) - 0.5) * 0.18;
                    float shake = (h21(float2(jid, floor(T * 26.0))) - 0.5) * 0.3 * (1.0 - smoothstep(0.1, 0.5, bT));   // 射出时左右抖动
                    float side = abs(jf + wob + shake) * (6.2831853 / NJ) * br;
                    float jw = (0.075 + 0.07 * rj) * (1.0 - br / max(Lj, 1e-3)) + 0.005;
                    float jfac = smoothstep(R * 0.45, R * 0.85, br) * (1.0 - smoothstep(Rj * 0.7, Rj, br)) * step(0.42, rj) * (1.0 - smoothstep(0.2, 0.5, bT));   // 更快消散
                    float jet = smoothstep(jw, jw * 0.25, side) * jfac;
                    float jetWide = smoothstep(jw * 2.2, jw * 0.6, side) * jfac;
                    float jn = fbm(dir * 3.0 + float2(br * 8.0 - T * 5.0, 1.0));
                    float jetB = jet * smoothstep(0.30, 0.42, br) * (0.65 + 0.7 * jn);          // 黑色火舌（不碰 LOGO 所在的中心）
                    col += bright * (0.8 + 0.6 * nr2) * 0.22 + flash * 0.4;
                    float armDetail = smoothstep(0.38, 0.70, fbm(dir * 7.0 + float2(br * 16.0 - T * 6.0, 5.0)));
                    col += saturate(jetWide - jet) * (0.25 + 1.1 * armDetail) * smoothstep(0.26, 0.4, br);   // 臂 = 带细节的白色火焰丝包着黑色的芯
                    // 爆炸的白光底：一团向外扩散的白光，黑色火舌才有东西可衬；黑焰从中间喷出来、切开白光
                    float back = smoothstep(0.95, 0.1, br) * smoothstep(0.0, 0.08, bT) * (1.0 - smoothstep(0.2, 0.7, bT));
                    col += back * 0.07;
                    // 飞散的火星拖影（参考黑魂灰烬迸溅）：大量细长锐利、带方向的白色拖影，头亮尾暗，从爆心向外飞，随时间变淡
                    float sparks = 0.0;
                    [unroll] for (int L = 0; L < 2; L++)
                    {
                        float M = (L == 0) ? 48.0 : 90.0;
                        float sc2 = atan2(bd.y, bd.x) / 6.2831853 * M + ((L == 0) ? 0.0 : 0.37);
                        float sid = floor(sc2); float sf = frac(sc2) - 0.5;
                        float r1 = h21(float2(sid, 11.3 + L)), r2 = h21(float2(sid, 29.1 + L)), r3 = h21(float2(sid, 47.7 + L));
                        float tt = max((T - 7.0) - r3 * 0.25, 0.0);                   // 各自的发射延迟
                        float head = 0.14 + (0.5 + 1.1 * r1) * tt;                    // 头部半径随时间向外
                        float len = 0.03 + 0.20 * r2 * r2;
                        float tpos = (br - (head - len)) / len;                       // 0 尾 → 1 头
                        float side2 = abs(sf) * (6.2831853 / M) * br;
                        float wd = 0.0014 + 0.0016 * r2;
                        float streak = smoothstep(wd, wd * 0.2, side2) * saturate(tpos) * step(tpos, 1.0) * saturate(tpos * tpos);
                        sparks += streak * step(0.55, h21(float2(sid, 5.5 + L))) * (0.5 + 1.3 * r1);
                    }
                    col += sparks * 0.8 * (1.0 - smoothstep(0.15, 0.55, bT));
                    // 黑核：爆心先是一个吞光的黑洞，核边一圈细亮的光晕 / 放射光线（参考图）
                    float coreEnv = smoothstep(0.0, 0.05, bT) * (1.0 - smoothstep(0.35, 0.8, bT));
                    float coreR = coreRv;
                    float core = smoothstep(coreR, coreR * 0.55, br) * coreEnv;
                    float rimGlow = exp(-pow((br - coreR) / 0.035, 2.0)) * coreEnv * (0.45 + 0.55 * fbm(dir * 6.0 + float2(T * 3.0, 2.0)));
                    col += rimGlow * 0.9;
                    col *= 1.0 - 0.97 * saturate(jetB);
                    col *= 1.0 - 0.95 * core * (1.0 - saturate(mk.r * mReveal * 3.0));
                    col *= 1.0 - 0.96 * saturate(mk.r * mReveal * back * 1.6);   // 白光里 LOGO 先是黑色剪影，白光退去后再变回白色
                }
                return col;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float T = _T;
                float zoom = 1.0 - 0.07 * saturate(min(T, 8.2) / _Duration);   // 推近在纯黑白 LOGO 出现时就停住，之后不再变化               // 镜头极缓慢推近
                float2 p = (uv - 0.5) * float2(_Aspect, 1.0) * zoom;
                float flatT = smoothstep(7.9, 9.0, T);
                float morph = smoothstep(7.0, 10.0, T), grow = smoothstep(3.0, 7.4, T);

                // ── 冲击波 = 空间扭曲：一圈向外扩散的透镜环，环经过的地方画面（含 LOGO、地面线）被折射弯曲 ──
                float bT0 = saturate((T - 7.0) / 1.4); float ringL = 0.0;
                if (T > 6.98 && bT0 < 1.0)
                {
                    float2 bdw = (p - float2(0.0, -0.02)) * float2(0.85, 1.0); float brw = length(bdw);
                    float2 dirw = bdw / max(brw, 1e-3);
                    float Rw = pow(bT0, 0.6) * 1.6;
                    float band = exp(-pow((brw - Rw) / 0.13, 2.0));
                    float rip = sin((brw - Rw) * 38.0) * band;
                    float amp = 0.075 * (1.0 - bT0 * 0.7) * smoothstep(0.0, 0.06, bT0);
                    p -= dirw * (band * 0.9 + rip * 0.4) * amp;
                    ringL = band * (1.0 - bT0) * 0.10;
                }
                float col = scene(p, uv) + ringL;

                // ── 漆黑大理石地面：高反光，倒映火焰和字 ──
                if (p.y < FloorY)
                {
                    float dy = FloorY - p.y;
                    float2 pm = float2(p.x, FloorY + dy);                          // 关于接触线镜像
                    // 大理石纹理：极暗的细脉让倒影有轻微的明暗和横向拉丝
                    float vein = fbm(float2(p.x * 2.4 + 5.0, p.y * 11.0)) ;
                    float2 pw = pm + float2((vein - 0.5) * 0.012, 0.0);
                    float2 uvm = pw / (float2(_Aspect, 1.0) * zoom) + 0.5;
                    float refl = scene(pw, uvm);
                    float fade = exp(-dy * 3.2);                                    // 离接触线越远倒影越淡
                    col += refl * 0.62 * fade * (0.7 + 0.3 * vein) * (1.0 - flatT) * (1.0 - 0.9 * smoothstep(8.8, 10.5, T) * tex2Dlod(_Mask, float4(p / float2(_Aspect, 1.0) + 0.5, 0, 0)).g);   // 文字所在处不叠倒影
                    // 地面上火焰的光池：椭圆形的冷白高光
                    float2 pool = float2(p.x * 0.8, dy * 2.4);
                    col += exp(-dot(pool, pool) * 7.0) * 0.07 * (smoothstep(1.8, 3.4, T) * 0.4 + grow) * (1.0 - morph * 0.6) * (1.0 - flatT);
                }
                else
                {
                    // 接触线以上是漆黑的虚空 / 墙，只在接触线附近有一丝地平线的反光
                    col += exp(-(p.y - FloorY) * 40.0) * 0.012 * smoothstep(2.0, 4.0, T) * (1.0 - flatT);
                }

                // ── 火星 / 灰烬 ──
                float2 emb = float2(0.0, -0.02);                            // 火星只集中在火焰（以及后来的 LOGO 火焰）周围
                float near = exp(-length((p - emb) * float2(4.5, 2.3)));
                float pa = smoothstep(2.4, 4.6, T) * near * near;
                float a = ash(p, 9.0, 0.10, 1.0, 0.07) * 0.5 + ash(p, 17.0, 0.18, 2.0, 0.06) * 0.8 + ash(p, 31.0, 0.28, 3.0, 0.055) * 0.7;
                col += a * pa * 0.45 * (1.0 - flatT);

                float3 rgb = lerp(float3(0.93, 0.96, 1.0) * col, col.xxx, flatT);                      // 偏冷的白
                rgb += (h21(uv * 997.0 + frac(T) * 31.7) - 0.5) * 0.02 * saturate(col * 6.0 + 0.1) * (1.0 - flatT);   // 颗粒噪点，纯黑处不加
                rgb *= smoothstep(1.4, 0.5, length(p * float2(1.0, 1.15)));
                rgb *= smoothstep(0.0, 0.6, T) * (1.0 - smoothstep(_Duration - 1.0, _Duration, T));
                return fixed4(saturate(rgb), 1);
            }
            ENDCG
        }
    }
}
