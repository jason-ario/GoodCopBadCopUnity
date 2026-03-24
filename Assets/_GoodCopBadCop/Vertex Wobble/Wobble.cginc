//# BLOCK: Wobble Properties
//# Inject @ Properties/Start
_WobbleAmplitude ("Wobble Amplitude", Float) = 0.001
_WobbleFrequency ("Wobble Frequency", Float) = 15.0
_WobbleSpeed ("Wobble Speed", Float) = 15.0

//# BLOCK: Wobble Variables
//# Inject @ Variables/Inside CBuffer
float _WobbleAmplitude;
float _WobbleFrequency;
float _WobbleSpeed;

//# BLOCK: Vertex Wobble Main
//# Inject @ Main Pass/Vertex Shader/Start

float wobbleTime = _Time.y * _WobbleSpeed;
float steppedTime = floor(wobbleTime);
float t = steppedTime / max(_WobbleSpeed, 0.0001);

float3 worldPos_main = mul(unity_ObjectToWorld, input.vertex).xyz;
float phase = dot(worldPos_main, float3(1.37, 2.11, 0.73));

float3 objectScale_main;
objectScale_main.x = length(unity_ObjectToWorld._m00_m10_m20);
objectScale_main.y = length(unity_ObjectToWorld._m01_m11_m21);
objectScale_main.z = length(unity_ObjectToWorld._m02_m12_m22);

float scaleCompensation_main = max(max(objectScale_main.x, objectScale_main.y), max(objectScale_main.z, 0.0001));
float wobble = sin(t * _WobbleFrequency + phase) * (_WobbleAmplitude / scaleCompensation_main);

input.vertex.xyz += input.normal * wobble;


//# BLOCK: Vertex Wobble Outline
//# Inject @ Outline Pass/Vertex Shader/Start

float wobbleTime_outline = _Time.y * _WobbleSpeed;
float steppedTime_outline = floor(wobbleTime_outline);
float t_outline = steppedTime_outline / max(_WobbleSpeed, 0.0001);

float3 worldPos_outline = mul(unity_ObjectToWorld, v.vertex).xyz;
float phase_outline = dot(worldPos_outline, float3(1.37, 2.11, 0.73));

float3 objectScale_outline;
objectScale_outline.x = length(unity_ObjectToWorld._m00_m10_m20);
objectScale_outline.y = length(unity_ObjectToWorld._m01_m11_m21);
objectScale_outline.z = length(unity_ObjectToWorld._m02_m12_m22);

float scaleCompensation_outline = max(max(objectScale_outline.x, objectScale_outline.y), max(objectScale_outline.z, 0.0001));
float wobble_outline = sin(t_outline * _WobbleFrequency + phase_outline) * (_WobbleAmplitude / scaleCompensation_outline);

v.vertex.xyz += v.normal * wobble_outline;


//# BLOCK: Vertex Wobble DepthShadow
//# Inject @ Depth + Shadow Caster Pass/Vertex Shader/Start

float wobbleTime_ds = _Time.y * _WobbleSpeed;
float steppedTime_ds = floor(wobbleTime_ds);
float t_ds = steppedTime_ds / max(_WobbleSpeed, 0.0001);

float3 worldPos_ds = mul(unity_ObjectToWorld, input.vertex).xyz;
float phase_ds = dot(worldPos_ds, float3(1.37, 2.11, 0.73));

float3 objectScale_ds;
objectScale_ds.x = length(unity_ObjectToWorld._m00_m10_m20);
objectScale_ds.y = length(unity_ObjectToWorld._m01_m11_m21);
objectScale_ds.z = length(unity_ObjectToWorld._m02_m12_m22);

float scaleCompensation_ds = max(max(objectScale_ds.x, objectScale_ds.y), max(objectScale_ds.z, 0.0001));
float wobble_ds = sin(t_ds * _WobbleFrequency + phase_ds) * (_WobbleAmplitude / scaleCompensation_ds);

input.vertex.xyz += input.normal * wobble_ds;