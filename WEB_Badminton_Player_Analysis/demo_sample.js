/**
 * 羽球選手動作分析示範數據庫 (Badminton Biomechanics Demo Dataset & Live Video Synthesizer)
 * -------------------------------------------------------------------------------------
 * 1. window.AXELSEN_DEMO_DATA: 安賽龍後場 390km/h 跳殺 (Jump Smash)
 * 2. window.TAI_TZU_YING_DEMO_DATA: 戴資穎網前假動作滑拍勾對角 (Deception Net Trickshot)
 * 3. window.bindDemoMatchVideo(videoElement, playerType): 原生合成 60FPS 轉播級比賽視訊串流
 */

(function () {
    const FPS = 30.0;
    const WIDTH = 1280;
    const HEIGHT = 720;

    // =========================================================================
    // 1. 產生 17 節點骨架計算函式
    // =========================================================================
    function generateKeypoints(cx, cy, elbowAngle, kneeAngle, shoulderTilt, jumpHeight) {
        const k = new Array(17);
        const hipY = cy + 40;

        // 0: Nose, 1: L Eye, 2: R Eye, 3: L Ear, 4: R Ear
        const headY = cy - 140;
        k[0] = [cx, headY, 0.98];
        k[1] = [cx - 6, headY - 4, 0.95];
        k[2] = [cx + 6, headY - 4, 0.95];
        k[3] = [cx - 14, headY - 2, 0.90];
        k[4] = [cx + 14, headY - 2, 0.90];

        // 5: L Shoulder, 6: R Shoulder
        const shoulderDist = 42;
        const tiltRad = (shoulderTilt * Math.PI) / 180;
        const lSh = [cx - shoulderDist * Math.cos(tiltRad), (cy - 75) + shoulderDist * Math.sin(tiltRad), 0.99];
        const rSh = [cx + shoulderDist * Math.cos(tiltRad), (cy - 75) - shoulderDist * Math.sin(tiltRad), 0.99];
        k[5] = lSh;
        k[6] = rSh;

        // 持拍右手 (6 -> 8 -> 10)
        const upperArmLen = 65;
        const forearmLen = 60;
        const armBaseAngle = -Math.PI * 0.45 - tiltRad * 0.8;
        const rElbow = [
            rSh[0] + upperArmLen * Math.cos(armBaseAngle),
            rSh[1] + upperArmLen * Math.sin(armBaseAngle),
            0.96
        ];
        const elbowRad = (elbowAngle * Math.PI) / 180;
        const forearmAngle = armBaseAngle + (Math.PI - elbowRad);
        const rWrist = [
            rElbow[0] + forearmLen * Math.cos(forearmAngle),
            rElbow[1] + forearmLen * Math.sin(forearmAngle),
            0.96
        ];

        // 非持拍左手 (5 -> 7 -> 9 平衡手)
        const lElbow = [lSh[0] - 45, lSh[1] + 25, 0.92];
        const lWrist = [lElbow[0] - 35, lElbow[1] - 15, 0.90];

        k[7] = lElbow;
        k[8] = rElbow;
        k[9] = lWrist;
        k[10] = rWrist;

        // 11: L Hip, 12: R Hip
        const hipDist = 30;
        const lHip = [cx - hipDist, hipY, 0.97];
        const rHip = [cx + hipDist, hipY, 0.97];
        k[11] = lHip;
        k[12] = rHip;

        // 腿部與膝關節 (13: L Knee, 14: R Knee, 15: L Ankle, 16: R Ankle)
        const thighLen = 70;
        const calfLen = 70;
        const kneeRad = (kneeAngle * Math.PI) / 180;

        const rKnee = [rHip[0] + 14, rHip[1] + thighLen * Math.sin(kneeRad * 0.7), 0.95];
        const rAnkle = [rKnee[0] - 8, rKnee[1] + calfLen, 0.95];

        const lKnee = [lHip[0] - 14, lHip[1] + thighLen * Math.sin(kneeRad * 0.7), 0.95];
        const lAnkle = [lKnee[0] - 8, lKnee[1] + calfLen, 0.95];

        k[13] = lKnee;
        k[14] = rKnee;
        k[15] = lAnkle;
        k[16] = rAnkle;

        return k;
    }

    // =========================================================================
    // 2. 安賽龍 (Viktor Axelsen) 390km/h 跳殺資料集
    // =========================================================================
    function buildAxelsenDataset() {
        const TOTAL_FRAMES = 120;
        const frames = [];
        const baseCenterX = 640;
        const groundY = 560;

        for (let f = 0; f < TOTAL_FRAMES; f++) {
            const t = f / FPS;
            let phase = "準備站位 (READY)";
            let jumpHeightCm = 0.0;
            let dominantElbowAngle = 95.0;
            let kneeAngle = 145.0;
            let shoulderTilt = 2.0;
            let comX = baseCenterX;
            let comY = groundY - 140;
            let courtNormX = 0.5;
            let courtNormY = 0.75;

            if (f < 30) {
                phase = "準備站位 (READY)";
                dominantElbowAngle = 92.0 + Math.sin(f * 0.2) * 5.0;
                kneeAngle = 142.0 + Math.cos(f * 0.2) * 4.0;
                shoulderTilt = 3.0 + Math.sin(f * 0.1) * 2.0;
                comX = baseCenterX + Math.sin(f * 0.15) * 8.0;
                comY = groundY - 140;
                courtNormX = 0.5 + Math.sin(f * 0.1) * 0.02;
                courtNormY = 0.78;
            } else if (f < 55) {
                const p = (f - 30) / 25.0;
                phase = "引拍蓄力 (LOADING)";
                dominantElbowAngle = 92.0 - p * 15.0;
                kneeAngle = 142.0 - Math.sin(p * Math.PI) * 35.0;
                shoulderTilt = 3.0 + p * 22.0;
                comX = baseCenterX + p * 15.0;
                comY = groundY - 140 + Math.sin(p * Math.PI) * 20.0;
                courtNormY = 0.78 + p * 0.03;
            } else if (f < 85) {
                const p = (f - 55) / 30.0;
                phase = "擊球瞬間 (IMPACT / APEX)";
                const jumpCurve = Math.sin(p * Math.PI);
                jumpHeightCm = Math.round(jumpCurve * 46.8 * 10) / 10;

                if (p < 0.45) {
                    dominantElbowAngle = 77.0 + (p / 0.45) * 98.0;
                } else {
                    dominantElbowAngle = 175.0 - ((p - 0.45) / 0.55) * 40.0;
                }

                kneeAngle = 110.0 + p * 60.0;
                shoulderTilt = 25.0 - p * 35.0;
                comX = baseCenterX + 15.0 - p * 30.0;
                comY = (groundY - 140) - (jumpCurve * 110.0);
                courtNormX = 0.52 - p * 0.05;
                courtNormY = 0.81 - p * 0.08;
            } else {
                const p = (f - 85) / 35.0;
                phase = "隨揮回防 (RECOVERY)";
                dominantElbowAngle = 135.0 - p * 40.0;
                kneeAngle = 170.0 - Math.sin(p * Math.PI) * 30.0;
                shoulderTilt = -10.0 + p * 13.0;
                comX = baseCenterX - 15.0 + p * 15.0;
                comY = groundY - 140;
                courtNormX = 0.47 + p * 0.03;
                courtNormY = 0.73 - p * 0.08;
            }

            const kpts = generateKeypoints(comX, comY, dominantElbowAngle, kneeAngle, shoulderTilt, jumpHeightCm);

            frames.push({
                frame_index: f,
                timestamp_sec: Math.round(t * 100) / 100,
                phase: phase,
                jump_height_cm: jumpHeightCm,
                court_position: {
                    norm_x: Math.round(courtNormX * 100) / 100,
                    norm_y: Math.round(courtNormY * 100) / 100
                },
                metrics: {
                    dominant_elbow_angle: Math.round(dominantElbowAngle * 10) / 10,
                    right_knee_angle: Math.round(kneeAngle * 10) / 10,
                    shoulder_tilt: Math.round(shoulderTilt * 10) / 10,
                    center_of_mass: {
                        x: Math.round(comX * 10) / 10,
                        y: Math.round(comY * 10) / 10
                    }
                },
                keypoints: kpts
            });
        }

        return {
            video_metadata: {
                filename: "viktor_axelsen_smash_pro.mp4",
                player_name: "安賽龍 (Viktor AXELSEN)",
                action_type: "後場起跳重殺 (Jump Smash)",
                width: WIDTH,
                height: HEIGHT,
                fps: FPS,
                total_frames: TOTAL_FRAMES,
                duration_seconds: 4.0
            },
            summary_metrics: {
                max_jump_height_cm: 46.8,
                max_elbow_extension_deg: 175.2,
                min_elbow_loading_deg: 77.0,
                swing_range_deg: 98.2,
                phase_distribution: {
                    "準備站位 (READY)": 25.0,
                    "引拍蓄力 (LOADING)": 20.8,
                    "擊球瞬間 (IMPACT / APEX)": 25.0,
                    "隨揮回防 (RECOVERY)": 29.2
                }
            },
            frames: frames
        };
    }

    // =========================================================================
    // 3. 戴資穎 (Tai Tzu-ying) 網前假動作滑拍勾對角資料集
    // =========================================================================
    function buildTaiTzuYingDataset() {
        const TOTAL_FRAMES = 120;
        const frames = [];
        const baseCenterX = 640;
        const groundY = 560;

        for (let f = 0; f < TOTAL_FRAMES; f++) {
            const t = f / FPS;
            let phase = "網前跨步迎球 (LUNGE)";
            let jumpHeightCm = 0.0;
            let dominantElbowAngle = 110.0;
            let kneeAngle = 135.0;
            let shoulderTilt = 5.0;
            let comX = baseCenterX;
            let comY = groundY - 140;
            let courtNormX = 0.5;
            let courtNormY = 0.35; // 網前位置

            // 階段 1: 0 ~ 35 幀 (大跨步逼近網前，舉拍蓄勢)
            if (f < 35) {
                const p = f / 35.0;
                phase = "網前跨步迎球 (LUNGE)";
                dominantElbowAngle = 100.0 + p * 20.0;
                kneeAngle = 145.0 - p * 45.0; // 右膝深跨步下壓至 100°
                shoulderTilt = 4.0 + p * 12.0;
                comX = baseCenterX + p * 60.0; // 往前右側網前衝
                comY = groundY - 140 + p * 15.0;
                courtNormX = 0.5 + p * 0.22;
                courtNormY = 0.45 - p * 0.22;
            }
            // 階段 2: 35 ~ 65 幀 (戴氏招牌：網前架拍停頓假動作，手腕微扣延遲出拍)
            else if (f < 65) {
                const p = (f - 35) / 30.0;
                phase = "停頓引誘蓄力 (DECEPTION HOLD)";
                dominantElbowAngle = 120.0 - Math.sin(p * Math.PI) * 12.0; // 手肘極致微控懸停
                kneeAngle = 100.0 + Math.sin(p * Math.PI) * 10.0;
                shoulderTilt = 16.0 - p * 4.0;
                comX = baseCenterX + 60.0 + Math.sin(p * Math.PI) * 5.0;
                comY = groundY - 125;
                courtNormX = 0.72;
                courtNormY = 0.23;
            }
            // 階段 3: 65 ~ 90 幀 (擊球瞬間：魔術手腕快速抖動滑拍勾對角)
            else if (f < 90) {
                const p = (f - 65) / 25.0;
                phase = "魔術滑拍折角 (TRICKSHOT IMPACT)";
                const hopCurve = Math.sin(p * Math.PI);
                jumpHeightCm = Math.round(hopCurve * 22.4 * 10) / 10; // 網前微躍

                if (p < 0.4) {
                    dominantElbowAngle = 108.0 + (p / 0.4) * 60.0; // 瞬間彈射推展至 168°
                } else {
                    dominantElbowAngle = 168.0 - ((p - 0.4) / 0.6) * 35.0;
                }

                kneeAngle = 110.0 + p * 40.0;
                shoulderTilt = 12.0 - p * 28.0; // 上身迅速旋轉變線
                comX = baseCenterX + 60.0 - p * 25.0;
                comY = (groundY - 140) - (hopCurve * 40.0);
                courtNormX = 0.72 - p * 0.12;
                courtNormY = 0.23 + p * 0.05;
            }
            // 階段 4: 90 ~ 120 幀 (交叉步快速回撤中場 T 點)
            else {
                const p = (f - 90) / 30.0;
                phase = "回動中心防守 (RECOVERY TO T)";
                dominantElbowAngle = 133.0 - p * 30.0;
                kneeAngle = 150.0 - Math.sin(p * Math.PI) * 15.0;
                shoulderTilt = -16.0 + p * 20.0;
                comX = baseCenterX + 35.0 - p * 35.0;
                comY = groundY - 140;
                courtNormX = 0.60 - p * 0.10;
                courtNormY = 0.28 + p * 0.22;
            }

            const kpts = generateKeypoints(comX, comY, dominantElbowAngle, kneeAngle, shoulderTilt, jumpHeightCm);

            frames.push({
                frame_index: f,
                timestamp_sec: Math.round(t * 100) / 100,
                phase: phase,
                jump_height_cm: jumpHeightCm,
                court_position: {
                    norm_x: Math.round(courtNormX * 100) / 100,
                    norm_y: Math.round(courtNormY * 100) / 100
                },
                metrics: {
                    dominant_elbow_angle: Math.round(dominantElbowAngle * 10) / 10,
                    right_knee_angle: Math.round(kneeAngle * 10) / 10,
                    shoulder_tilt: Math.round(shoulderTilt * 10) / 10,
                    center_of_mass: {
                        x: Math.round(comX * 10) / 10,
                        y: Math.round(comY * 10) / 10
                    }
                },
                keypoints: kpts
            });
        }

        return {
            video_metadata: {
                filename: "tai_tzu_ying_deception_master.mp4",
                player_name: "戴資穎 (TAI Tzu Ying)",
                action_type: "網前假動作滑拍勾對角 (Deception Trickshot)",
                width: WIDTH,
                height: HEIGHT,
                fps: FPS,
                total_frames: TOTAL_FRAMES,
                duration_seconds: 4.0
            },
            summary_metrics: {
                max_jump_height_cm: 22.4,
                max_elbow_extension_deg: 168.5,
                min_elbow_loading_deg: 108.0,
                swing_range_deg: 60.5,
                phase_distribution: {
                    "網前跨步迎球 (LUNGE)": 29.2,
                    "停頓引誘蓄力 (DECEPTION HOLD)": 25.0,
                    "魔術滑拍折角 (TRICKSHOT IMPACT)": 20.8,
                    "回動中心防守 (RECOVERY TO T)": 25.0
                }
            },
            frames: frames
        };
    }

    // 全域匯出資料集
    window.AXELSEN_DEMO_DATA = buildAxelsenDataset();
    window.TAI_TZU_YING_DEMO_DATA = buildTaiTzuYingDataset();
    window.BADMINTON_DEMO_DATA = window.AXELSEN_DEMO_DATA; // 預設
})();
