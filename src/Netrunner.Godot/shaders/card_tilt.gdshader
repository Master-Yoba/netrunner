// Perspective tilt for a card being dragged (playtest, 30 September: a flat 2D lean felt cheap).
// Every vertex the card draws is turned about the card's centre, yaw about its vertical axis and
// pitch about its horizontal one, then projected with a simple perspective, so the card swings like
// a card held at an angle. A faint sheen follows the turn.
shader_type canvas_item;

uniform vec2 card_size = vec2(100.0, 140.0);
uniform float yaw = 0.0;
uniform float pitch = 0.0;
uniform float depth = 650.0;
uniform float sheen = 0.0;

varying vec2 local;

void vertex() {
	local = VERTEX / card_size;
	vec3 p = vec3(VERTEX - card_size * 0.5, 0.0);
	float cy = cos(yaw);
	float sy = sin(yaw);
	p = vec3(cy * p.x + sy * p.z, p.y, -sy * p.x + cy * p.z);
	float cp = cos(pitch);
	float sp = sin(pitch);
	p = vec3(p.x, cp * p.y - sp * p.z, sp * p.y + cp * p.z);
	float scale = depth / max(depth + p.z, 1.0);
	VERTEX = card_size * 0.5 + p.xy * scale;
}

void fragment() {
	// A soft diagonal band of light that slides across the card as it turns.
	float centre = 0.5 - yaw * 1.6 + pitch * 1.2;
	float band = 1.0 - smoothstep(0.0, 0.32, abs(local.x * 0.8 + local.y * 0.4 - centre));
	COLOR.rgb += vec3(band * sheen * 0.22) * COLOR.a;
}
