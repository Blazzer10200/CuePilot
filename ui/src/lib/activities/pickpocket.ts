export type PickpocketTarget = "White" | "Purple" | "Red" | "PaleGreen" | "Blue" | "Yellow";
export type PickpocketRecording = "grab" | "miss" | "part" | "live";

// Item names, colors, and spans belong to the supplied reference recording.
// These names are clip observations, not live item-recognition rules.
export const pickpocketTargets = [
  { id: "White", name: "Rope", colorName: "White", color: "#dce2e6", left: 728, right: 808, windowMs: 283, difficulty: "Wide window" },
  { id: "Purple", name: "Luxury Watch", colorName: "Purple", color: "#ca8afb", left: 888, right: 903, windowMs: 53, difficulty: "Precise timing" },
  { id: "Red", name: "Ruby", colorName: "Red", color: "#f0757e", left: 1022, right: 1026, windowMs: 14, difficulty: "Very narrow window" },
  { id: "PaleGreen", name: "Broken Electronic Part", colorName: "Pale green", color: "#c1d99a", left: 1132, right: 1172, windowMs: 141, difficulty: "Moderate window" },
] as const;

export const pickpocketRecordings = {
  grab: { label: "Clip 1 · Watch grab", outcome: "Recorded grab · Luxury Watch", durationMs: 2000, activeMs: 300, resultMs: 1083.3, speed: 283, targets: pickpocketTargets },
  miss: { label: "Clip 2 · Missed attempt", outcome: "Recorded miss", durationMs: 3900, activeMs: 283.333, resultMs: 3083.333, speed: 290, targets: [
    { id: "Purple", name: "Ring", colorName: "Purple", color: "#ca8afb", left: 756, right: 780, windowMs: 83, difficulty: "Precise timing" },
    { id: "Blue", name: "Cuff Medicine", colorName: "Blue", color: "#80b6fa", left: 879, right: 914, windowMs: 121, difficulty: "Moderate window" },
    { id: "Yellow", name: "TNT Recipe", colorName: "Yellow", color: "#ecd66c", left: 1022, right: 1025, windowMs: 10, difficulty: "Very narrow window" },
    { id: "White", name: "Loose Change", colorName: "White", color: "#dce2e6", left: 1106, right: 1198, windowMs: 317, difficulty: "Wide window" },
  ] },
  part: { label: "Clip 3 · Electronic part", outcome: "Recorded grab · Electronic Part", durationMs: 2800, activeMs: 283.334, resultMs: 2000, speed: 286, targets: [
    { id: "Yellow", name: "TNT Recipe", colorName: "Yellow", color: "#ecd66c", left: 768, right: 770, windowMs: 7, difficulty: "Very narrow window" },
    { id: "White", name: "Loose Change", colorName: "White", color: "#dce2e6", left: 850, right: 942, windowMs: 322, difficulty: "Wide window" },
    { id: "Red", name: "Ruby", colorName: "Red", color: "#f0757e", left: 1022, right: 1026, windowMs: 14, difficulty: "Very narrow window" },
    { id: "PaleGreen", name: "Broken Electronic Part", colorName: "Pale green", color: "#c1d99a", left: 1132, right: 1172, windowMs: 140, difficulty: "Moderate window" },
  ] },
  live: { label: "Live test · Blue grab", outcome: "Recorded grab · Blue region", durationMs: 4092.7, activeMs: 300.4, resultMs: 3547.6, speed: 388, targets: [
    { id: "Red", name: "Ruby", colorName: "Red", color: "#f0757e", left: 639, right: 643, windowMs: 10, difficulty: "Very narrow window" },
    { id: "Blue", name: "Pocket Watch", colorName: "Blue", color: "#80b6fa", left: 784, right: 838, windowMs: 139, difficulty: "Moderate window" },
    { id: "PaleGreen", name: "Broken Electronic Part", colorName: "Pale green", color: "#c1d99a", left: 954, right: 1008, windowMs: 139, difficulty: "Moderate window" },
    { id: "White", name: "Wrist Band", colorName: "White", color: "#dce2e6", left: 1109, right: 1193, windowMs: 216, difficulty: "Wide window" },
  ] },
} as const;

export const knownItemCount = new Set(Object.values(pickpocketRecordings).flatMap(clip => clip.targets.map(target => target.name))).size;

export const targetStorageKey = "cuepilot.pickpocket.target";
export const recordingStorageKey = "cuepilot.pickpocket.recording";
export function readRecording(value: string | null): PickpocketRecording {
  return value === "grab" || value === "miss" || value === "part" ? value : "live";
}
export function readTarget(value: string | null, recording: PickpocketRecording = "grab"): PickpocketTarget {
  return pickpocketRecordings[recording].targets.some(target => target.id === value) ? value as PickpocketTarget : recording === "grab" ? "Purple" : "White";
}

export const traceDurationMs = 2000;
export const barPosition = (x: number, left = 672, width = 576) => Math.max(0, Math.min(100, (x - left) / width * 100));

// Native presentation timestamps and measured marker positions from the user clip.
// Interpolation is a visual replay, never a game input/timing decision.
const trace = [
  [0, 673.5], [266.6, 673.5], [316.6, 677], [366.6, 692.5], [416.6, 708],
  [466.6, 721], [516.6, 735], [566.6, 749], [616.6, 764], [666.6, 778.5],
  [716.6, 794], [766.6, 807], [816.6, 820.5], [866.6, 833], [916.6, 849.5],
  [966.6, 864], [1016.6, 878.5], [1066.6, 892], [1083.3, 896], [2000, 896],
];

// Clip two: native observations relative to 3.816667 s; sparse points are
// interpolated for display, including the return pass and frozen missed result.
const missTrace = [
  [0, 674], [283.333, 673], [333.333, 688], [1483.333, 1024],
  [1500, 1030], [1550, 1042], [2083.333, 1194], [2100, 1204],
  [2150, 1216], [2266.666, 1244], [2283.333, 1238], [2333.333, 1224],
  [2416.666, 1200], [2433.333, 1194], [2500, 1178], [2533.333, 1168],
  [2550, 1162], [2566.666, 1160], [3033.333, 1024], [3050, 1018],
  [3066.666, 1018], [3083.333, 1016], [3900, 1016],
];

// Clip three: native observations relative to 0.983333 s.
const partTrace = [
  [0, 674], [33.334, 672], [283.334, 672], [316.667, 682],
  [333.334, 688], [383.334, 702], [516.667, 734], [616.667, 764],
  [766.667, 808], [783.334, 814], [816.667, 822], [866.667, 836],
  [916.667, 851], [1033.334, 886], [1050, 890], [1083.334, 900],
  [1100, 906], [1150, 920], [1200, 934], [1216.667, 938],
  [1266.667, 952], [1283.334, 956], [1300, 962], [1333.334, 972],
  [1350, 978], [1400, 992], [1433.334, 1002], [1450, 1006],
  [1466.667, 1012], [1483.334, 1016], [1900, 1136], [1916.667, 1140],
  [2000, 1160], [2800, 1160],
];

// First live trace: sampled positions/times, including the return pass. Not video frames.
const liveTrace = [
  [0, 515], [300.4, 516], [461.1, 574], [615.3, 635], [772.4, 699],
  [908.1, 751], [1065.4, 813], [1225, 873], [1380.1, 933], [1536.2, 990],
  [1692.3, 1051], [1845.4, 1113], [2001.5, 1174], [2156.7, 1234],
  [2298.1, 1267], [2453.4, 1207], [2611.1, 1146], [2767.8, 1089],
  [2921.1, 1027], [3080.8, 965], [3237.9, 906], [3392.8, 846],
  [3547.6, 792], [4092.7, 792],
];

export function referenceAt(time: number, recording: PickpocketRecording = "grab") {
  const clip = pickpocketRecordings[recording];
  const points = recording === "grab" ? trace : recording === "miss" ? missTrace : recording === "part" ? partTrace : liveTrace;
  const elapsed = Number.isFinite(time) ? Math.max(0, Math.min(clip.durationMs, time)) : 0;
  const right = points.findIndex(([at]) => at >= elapsed);
  const next = points[Math.max(0, right)];
  const previous = points[Math.max(0, right - 1)];
  const ratio = next[0] === previous[0] ? 0 : (elapsed - previous[0]) / (next[0] - previous[0]);
  return {
    markerX: previous[1] + (next[1] - previous[1]) * ratio,
    direction: next[1] < previous[1] ? "left" : "right",
    state: elapsed < clip.activeMs ? "Preparing" : elapsed < clip.resultMs ? "Active" : recording === "miss" ? "Missed" : "Grabbed",
  } as const;
}
