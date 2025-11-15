#ifndef BEAM_NOISE_GEN_DEFINED
#define BEAM_NOISE_GEN_DEFINED

void _Hash_Tchou_3_1_uint(uint3 v, out uint o)
{
	// ~15 alu (3 mul)
	v.x ^= 1103515245U;
	v.y ^= v.x + v.z;
	v.y = v.y * 134775813;
	v.z += v.x ^ v.y;
	v.y += v.x ^ v.z;
	v.x += v.y * v.z;
	v.x = v.x * 0x27d4eb2du;
	o = v.x;
}

void _Hash_Tchou_3_1_float(float3 i, out float o)
{
	uint r;
	uint3 v = (uint3) (int3) round(i);
	_Hash_Tchou_3_1_uint(v, r);
	o = (r >> 8) * (1.0 / float(0x00ffffff));
}

float _Beam_SimpleNoise_ValueNoise_Deterministic_float(float3 uv)
{
	float3 i = floor(uv);
	float3 f = frac(uv);
	f = f * f * (3.0 - 2.0 * f);
	uv = abs(frac(uv) - 0.5);
	float3 c0 = i + float3(0.0, 0.0, 0.0);
	float3 c1 = i + float3(1.0, 0.0, 0.0);
	float3 c2 = i + float3(0.0, 1.0, 0.0);
	float3 c3 = i + float3(1.0, 1.0, 0.0);
	float3 c4 = i + float3(0.0, 0.0, 1.0);
	float3 c5 = i + float3(1.0, 0.0, 1.0);
	float3 c6 = i + float3(0.0, 1.0, 1.0);
	float3 c7 = i + float3(1.0, 1.0, 1.0);

	float r0; _Hash_Tchou_3_1_float(c0, r0);
	float r1; _Hash_Tchou_3_1_float(c1, r1);
	float r2; _Hash_Tchou_3_1_float(c2, r2);
	float r3; _Hash_Tchou_3_1_float(c3, r3);
	float r4; _Hash_Tchou_3_1_float(c4, r4);
	float r5; _Hash_Tchou_3_1_float(c5, r5);
	float r6; _Hash_Tchou_3_1_float(c6, r6);
	float r7; _Hash_Tchou_3_1_float(c7, r7);

	float bottomOfGrid0 = lerp(r0, r1, f.x);
	float topOfGrid0 = lerp(r2, r3, f.x);
	float t0 = lerp(bottomOfGrid0, topOfGrid0, f.y);

	float bottomOfGrid1 = lerp(r4, r5, f.x);
	float topOfGrid1 = lerp(r6, r7, f.x);
	float t1 = lerp(bottomOfGrid1, topOfGrid1, f.y);

	float t = lerp(t0, t1, f.z);

	return t;
}

void _Beam_SimpleNoise_Deterministic_float(float3 UV, float Scale, out float Out)
{
	float freq, amp;
	Out = 0.0f;
	freq = pow(2.0, float(0));
	amp = pow(0.5, float(3 - 0));
	Out += _Beam_SimpleNoise_ValueNoise_Deterministic_float(float3(UV.xyz * (Scale / freq))) * amp;
	freq = pow(2.0, float(1));
	amp = pow(0.5, float(3 - 1));
	Out += _Beam_SimpleNoise_ValueNoise_Deterministic_float(float3(UV.xyz * (Scale / freq))) * amp;
	freq = pow(2.0, float(2));
	amp = pow(0.5, float(3 - 2));
	Out += _Beam_SimpleNoise_ValueNoise_Deterministic_float(float3(UV.xyz * (Scale / freq))) * amp;
}

float SimpleNoise(float3 positionWS, float noiseSize)
{
	float noise;
	_Beam_SimpleNoise_Deterministic_float(positionWS, noiseSize, noise);
	return noise;
}
#endif