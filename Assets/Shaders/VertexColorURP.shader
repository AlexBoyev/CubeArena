Shader "PocketHeist/VertexColorURP"
{
    // Kenney Furniture Kit's rugRectangle has no texture atlas - colour comes from
    // per-vertex colour data baked into the mesh, which a stock URP/Lit material doesn't
    // read at all (renders flat white/grey - see docs/ASSETS.md's "Vertex-colour note").
    // Deferred from Milestone 2 to Milestone 4's real-asset pass, per docs/PROGRESS.md.
    //
    // Hand-written rather than a Shader Graph asset: Shader Graph assets are authored
    // interactively in the Editor's graph window, not scriptable/batch-buildable the way
    // every other asset in this project is produced (CLAUDE.md: no hand-editing scene/
    // prefab/meta files, everything through Editor scripts) - a plain .shader source file
    // is ordinary text source code like any .cs file, so it fits that same convention.
    // Approximate single-directional-light Lambertian shading (URP's main light only, no
    // full PBR/shadow receiving) - good enough for "the rug shows its real baked colours
    // instead of flat white" at this milestone's placeholder-art fidelity; revisit with a
    // full Lit-equivalent (or a real Shader Graph) if a later milestone needs the rug to
    // receive shadows/match other lit surfaces more closely.
    Properties
    {
        _Brightness("Brightness", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR;
            };

            float _Brightness;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positionInputs.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.normalWS);
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 lit = IN.color.rgb * (mainLight.color * ndotl + unity_AmbientSky.rgb) * _Brightness;
                return half4(lit, IN.color.a);
            }
            ENDHLSL
        }
    }
}
