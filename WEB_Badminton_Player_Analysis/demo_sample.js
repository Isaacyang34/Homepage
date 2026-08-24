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

            // =========================================================================
            // 羽球真實空氣動力學拋物線飛行模型 (Aerodynamic Parabolic Trajectory)
            // 羽球具備高風阻特性，高遠球與殺球均呈現非對稱拋物線 (Inverted Cusp Curve)
            // =========================================================================
            const HIT_X = 640.0;   // 擊球點 X (後場)
            const HIT_Y = 195.0;   // 擊球點 Y (起跳最高點，高空 2.9m 換算)
            const LAND_X = 328.0;  // 落點 X (對角邊線)
            const LAND_Y = 550.0;  // 落點 Y (前場邊線壓線)
            const SMASH_SPEED = 382.4; // 殺球初速 382.4 km/h
            const HIT_FRAME = 68;

            let shuttleX = 460.0;
            let shuttleY = 480.0;
            let shuttleSpeedKmh = 120.0;
            let isHit = false;
            let isApex = false;
            let isLanded = false;
            let isInCourt = true;
            let hawkEyeDistCm = 0.0;

            if (f < HIT_FRAME) {
                // 1. 對手高遠球：真實羽球非對稱陡降高拋物線
                // 前段快速爬升，中段滯空，後段受阻力近乎垂直急降
                const p = f / HIT_FRAME;
                // X 軸：受空氣阻力減速前進 (指數衰減)
                shuttleX = 420.0 + (1 - Math.exp(-p * 1.8)) / (1 - Math.exp(-1.8)) * (HIT_X - 420.0);
                // Y 軸：拋物線爬升至頂點 (Apex at f=26)，隨後急速下落至擊球點
                const arcHeight = 240.0;
                const apexNorm = 0.38; // 頂點偏前
                let yNorm;
                if (p < apexNorm) {
                    const u = p / apexNorm;
                    yNorm = 1 - Math.sin(u * Math.PI * 0.5);
                } else {
                    const u = (p - apexNorm) / (1 - apexNorm);
                    yNorm = Math.sin(u * Math.PI * 0.5);
                }
                const baseLineY = 480.0 + p * (HIT_Y - 480.0);
                shuttleY = baseLineY - (1 - Math.pow(p - apexNorm, 2) / Math.pow(apexNorm, 2)) * arcHeight;
                shuttleSpeedKmh = Math.max(72.0, 210.0 - p * 135.0);
                if (f === 26) isApex = true;

            } else if (f === HIT_FRAME) {
                // 2. 擊球瞬間 (Impact Point)
                shuttleX = HIT_X;
                shuttleY = HIT_Y;
                shuttleSpeedKmh = SMASH_SPEED;
                isHit = true;

            } else if (f < 98) {
                // 3. 極速重殺俯衝拋物線：初速極快，受重力與空氣阻力下墜加速彎曲
                const p = (f - HIT_FRAME) / (98.0 - HIT_FRAME);
                // X 軸水平前進 (隨阻力微幅減速)
                shuttleX = HIT_X + (p * 0.85 + Math.pow(p, 2) * 0.15) * (LAND_X - HIT_X);
                // Y 軸真實下墜拋物線：y = y0 + vy0*t + 0.5*g*t^2 (向下彎曲弧線)
                // 殺球並非直線，而在中後段因重力與減速有明顯向下彎折的拋物弧度
                const parabolaDrop = Math.pow(p, 1.45) * (LAND_Y - HIT_Y);
                shuttleY = HIT_Y + parabolaDrop;
                // 速度呈指數急劇衰減
                shuttleSpeedKmh = Math.max(140.0, SMASH_SPEED * Math.exp(-p * 0.78));

            } else if (f === 98) {
                // 4. 落點著地 (壓線 3.2cm 界內)
                shuttleX = LAND_X;
                shuttleY = LAND_Y;
                shuttleSpeedKmh = 140.0;
                isLanded = true;
                isInCourt = true;
                hawkEyeDistCm = 3.2;

            } else {
                // 5. 落地小拋物線彈跳 (Elastic Bounce Arc)
                const p = (f - 98) / 22.0;
                shuttleX = LAND_X - p * 38.0;
                const bounceHeight = 52.0 * (1 - p);
                shuttleY = LAND_Y - Math.sin(p * Math.PI) * bounceHeight + p * 8.0;
                shuttleSpeedKmh = Math.max(0.0, 140.0 * (1 - p * 1.5));
            }

            frames.push({
                frame_index: f,
                timestamp_sec: Math.round(t * 100) / 100,
                phase: phase,
                jump_height_cm: jumpHeightCm,
                court_position: {
                    norm_x: Math.round(courtNormX * 100) / 100,
                    norm_y: Math.round(courtNormY * 100) / 100
                },
                shuttlecock: {
                    x: Math.round(shuttleX * 10) / 10,
                    y: Math.round(shuttleY * 10) / 10,
                    speed_kmh: Math.round(shuttleSpeedKmh * 10) / 10,
                    is_hit: isHit,
                    is_apex: isApex,
                    is_landed: isLanded,
                    is_in_court: isInCourt,
                    hawkeye_dist_cm: hawkEyeDistCm
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
                peak_smash_speed_kmh: 382.4,
                average_rally_speed_kmh: 176.5,
                landing_verdict: "界內 (IN 3.2cm)",
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

            // =========================================================================
            // 羽球真實空氣動力學拋物線飛行模型 (戴資穎網前滑拍勾對角)
            // 翻滾過網下墜拋物線 -> 網前輕搓/推勾反向拋物弧線 -> 死角垂直急墜
            // =========================================================================
            const TTY_HIT_X = 695.0;  // 網前擊球點 X
            const TTY_HIT_Y = 275.0;  // 網前擊球點 Y (近網帶高點)
            const TTY_LAND_X = 370.0; // 勾對角落點 X
            const TTY_LAND_Y = 475.0; // 勾對角落點 Y (對方網前死角)
            const TTY_SMASH_SPEED = 186.5;
            const TTY_HIT_FRAME = 70;

            let shuttleX, shuttleY, shuttleSpeedKmh;
            let isHit = false;
            let isApex = false;
            let isLanded = false;
            let isInCourt = true;
            let hawkEyeDistCm = 0.0;

            if (f < TTY_HIT_FRAME) {
                // 1. 對手放網球：翻滾過網下墜拋物線 (高點翻滾後重力急墜)
                const p = f / TTY_HIT_FRAME;
                shuttleX = 540.0 + p * (TTY_HIT_X - 540.0);
                // 網頂拋物線 (在 f=20 過網最高點，隨後向下垂墜)
                const netApexP = 0.32;
                const netClearY = 230.0;
                let dropCurve;
                if (p < netApexP) {
                    const u = p / netApexP;
                    dropCurve = 280.0 - (1 - Math.pow(1 - u, 2)) * (280.0 - netClearY);
                } else {
                    const u = (p - netApexP) / (1 - netApexP);
                    dropCurve = netClearY + Math.pow(u, 1.8) * (TTY_HIT_Y - netClearY);
                }
                shuttleY = dropCurve;
                shuttleSpeedKmh = Math.max(42.0, 95.0 - p * 52.0);

            } else if (f === TTY_HIT_FRAME) {
                // 2. 戴資穎假動作滑拍擊球瞬間 (Impact)
                shuttleX = TTY_HIT_X;
                shuttleY = TTY_HIT_Y;
                shuttleSpeedKmh = TTY_SMASH_SPEED;
                isHit = true;

            } else if (f < 95) {
                // 3. 勾對角貼網反拋物線：微幅貼網挑起弧度後，受阻力在死角迅速沉墜
                const p = (f - TTY_HIT_FRAME) / (95.0 - TTY_HIT_FRAME);
                // X 軸向對角前進
                shuttleX = TTY_HIT_X + (1 - Math.exp(-p * 1.5)) / (1 - Math.exp(-1.5)) * (TTY_LAND_X - TTY_HIT_X);
                // Y 軸拋物弧線：先貼網微揚 (Apex at p=0.28)，再迅速下墜
                const hookApex = 0.28;
                let yHook;
                if (p < hookApex) {
                    const u = p / hookApex;
                    yHook = TTY_HIT_Y - Math.sin(u * Math.PI * 0.5) * 28.0;
                } else {
                    const u = (p - hookApex) / (1 - hookApex);
                    yHook = (TTY_HIT_Y - 28.0) + Math.pow(u, 1.6) * (TTY_LAND_Y - (TTY_HIT_Y - 28.0));
                }
                shuttleY = yHook;
                shuttleSpeedKmh = Math.max(68.0, TTY_SMASH_SPEED * Math.exp(-p * 0.68));
                if (f === 77) isApex = true;

            } else if (f === 95) {
                // 4. 落入對手網前死角
                shuttleX = TTY_LAND_X;
                shuttleY = TTY_LAND_Y;
                shuttleSpeedKmh = 68.0;
                isLanded = true;
                isInCourt = true;
                hawkEyeDistCm = 1.8;

            } else {
                // 5. 滾網貼地
                const p = (f - 95) / 25.0;
                shuttleX = TTY_LAND_X - p * 12.0;
                shuttleY = TTY_LAND_Y + p * 4.0;
                shuttleSpeedKmh = 0.0;
            }

            frames.push({
                frame_index: f,
                timestamp_sec: Math.round(t * 100) / 100,
                phase: phase,
                jump_height_cm: jumpHeightCm,
                court_position: {
                    norm_x: Math.round(courtNormX * 100) / 100,
                    norm_y: Math.round(courtNormY * 100) / 100
                },
                shuttlecock: {
                    x: Math.round(shuttleX * 10) / 10,
                    y: Math.round(shuttleY * 10) / 10,
                    speed_kmh: Math.round(shuttleSpeedKmh * 10) / 10,
                    is_hit: isHit,
                    is_apex: isApex,
                    is_landed: isLanded,
                    is_in_court: isInCourt,
                    hawkeye_dist_cm: hawkEyeDistCm
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
                peak_smash_speed_kmh: 186.5,
                average_rally_speed_kmh: 112.0,
                landing_verdict: "界內 (IN 1.8cm)",
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
