// Surfaces which will be nearby behind a portal and cannot go through
// portals (e.g. a wall) should have a shader like this one. The key
// thing is to have a render queue lower than the Portal View shader.
//
// This shader is just the "New Surface Shader" except its render queue
// is changed to Geometry-200 and its FallBack is changed to "Standard".
//
// !!!!!!
// IMPORTANT: THE UNITY STANDARD SHADER CANNOT HAVE ITS RENDER QUEUE
// CHANGED FOR VRCHAT. IT WILL ALWAYS BE 2000 IN-GAME! It will appear
// to have whatever render queue you set in the editor, but VRChat
// will override it to 2000 in-game. That is why you need a custom
// shader like this one. Other popular shaders like Poiyomi will work
// too, just reduce the render queue.
//
// IMPORTANT 2: WHEN USING A DEFAULT "New Surface Shader", YOU MUST
// SET THE "FallBack" AT THE BOTTOM TO "Standard" OR ANOTHER SHADER
// THAT HAS A SHADOWCASTER AND IS INCLUDED WITH VRCHAT. The default
// fallback for "New Surface Shader" is "Diffuse", which is not in
// VRChat by default. Therefore, this won't have a shadowcaster, and
// therefore it won't do lighting or z-testing correctly in-game.
// !!!!!!

Shader "Aurycat/ExampleBackgroundSurfaceForPortalDepth"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-200" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo comes from a texture tinted by color
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            // Metallic and smoothness come from slider variables
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
