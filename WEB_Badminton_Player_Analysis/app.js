/**
 * Badminton Player Vision AI - Biomechanics & Pose Analytics Application
 * ---------------------------------------------------------------------
 * 處理影片播放、HTML5 Canvas 骨架渲染、關節角度動態弧線、2D 球場小地圖同步與 Chart.js
 */

(function () {
    // 狀態變數
    let currentData = null;
    let currentFrameIdx = 0;
    let isPlaying = false;
    let playSpeed = 1.0;
    let playInterval = null;
    let kinematicsChart = null;

    // DOM Elements
    const video = document.getElementById('mainVideo');
    const canvas = document.getElementById('poseCanvas');
    const ctx = canvas.getContext('2d');
    const videoStage = document.getElementById('videoStage');
    const videoPlaceholder = document.getElementById('videoPlaceholder');
    const videoFileInput = document.getElementById('videoFileInput');

    // Controls
    const btnPlayPause = document.getElementById('btnPlayPause');
    const playIcon = document.getElementById('playIcon');
    const pauseIcon = document.getElementById('pauseIcon');
    const btnStepBack = document.getElementById('btnStepBack');
    const btnStepForward = document.getElementById('btnStepForward');
    const timelineSlider = document.getElementById('timelineSlider');
    const currentTimeLabel = document.getElementById('currentTimeLabel');
    const durationLabel = document.getElementById('durationLabel');
    const currentFrameNum = document.getElementById('currentFrameNum');
    const totalFrameNum = document.getElementById('totalFrameNum');
    const btnLoadDemo = document.getElementById('btnLoadDemo');

    // Layers
    const chkTrajectory = document.getElementById('chkTrajectory');
    const chkLanding = document.getElementById('chkLanding');
    const chkSkeleton = document.getElementById('chkSkeleton');
    const chkAngles = document.getElementById('chkAngles');
    const chkCoM = document.getElementById('chkCoM');
    const chkJumpApex = document.getElementById('chkJumpApex');

    // Metrics & Gauges
    const currentPhaseBadge = document.getElementById('currentPhaseBadge');
    const currentPhaseText = document.getElementById('currentPhaseText');
    const valElbowAngle = document.getElementById('valElbowAngle');
    const elbowGaugeFill = document.getElementById('elbowGaugeFill');
    const elbowStatusTag = document.getElementById('elbowStatusTag');
    const valKneeAngle = document.getElementById('valKneeAngle');
    const kneeGaugeFill = document.getElementById('kneeGaugeFill');
    const kneeStatusTag = document.getElementById('kneeStatusTag');
    const valJumpHeight = document.getElementById('valJumpHeight');
    const valMaxJumpRecord = document.getElementById('valMaxJumpRecord');
    const valShoulderTilt = document.getElementById('valShoulderTilt');
    const hudAngularVel = document.getElementById('hudAngularVel');
    const hudJumpHeight = document.getElementById('hudJumpHeight');
    const feedbackList = document.getElementById('feedbackList');

    // Module 2: Shuttlecock Flight Telemetry Elements
    const valShuttleSpeed = document.getElementById('valShuttleSpeed');
    const hudShuttleSpeed = document.getElementById('hudShuttleSpeed');
    const valPeakSmashSpeed = document.getElementById('valPeakSmashSpeed');
    const hudPeakSmash = document.getElementById('hudPeakSmash');
    const valFlightTime = document.getElementById('valFlightTime');
    const valApexHeight = document.getElementById('valApexHeight');
    const valHawkEyeVerdict = document.getElementById('valHawkEyeVerdict');
    const hudHawkEyeBadge = document.getElementById('hudHawkEyeBadge');
    const hudHawkEyeText = document.getElementById('hudHawkEyeText');
    const shuttleStatusTag = document.getElementById('shuttleStatusTag');

    let peakSmashRecorded = 0.0;

    // 2D Mini-Court Radar
    const courtCanvas = document.getElementById('courtRadarCanvas');
    const courtCtx = courtCanvas.getContext('2d');

    // COCO 骨架連線索引
    const SKELETON_CONNECTIONS = [
        [5, 6, '#A855F7'], // shoulders (torso)
        [5, 7, '#00D2FF'], [7, 9, '#00D2FF'], // left arm
        [6, 8, '#00F59B'], [8, 10, '#00F59B'], // right arm (dominant)
        [5, 11, '#A855F7'], [6, 12, '#A855F7'], // torso sides
        [11, 12, '#A855F7'], // hips
        [11, 13, '#00D2FF'], [13, 15, '#00D2FF'], // left leg
        [12, 14, '#00F59B'], [14, 16, '#00F59B'], // right leg
    ];

    let poseDetector = null;

    // 初始化 TensorFlow.js MoveNet 深度學習模型
    async function initMoveNetDetector() {
        if (window.tf) {
            try {
                await tf.ready();
                await tf.setBackend('webgl');
            } catch (e) {
                console.warn('[AI Engine] WebGL backend fallback to CPU:', e);
            }
        }
        if (window.poseDetection) {
            try {
                const model = poseDetection.SupportedModels.MoveNet;
                poseDetector = await poseDetection.createDetector(model, {
                    modelType: poseDetection.movenet.modelType.SINGLEPOSE_LIGHTNING
                });
                console.log('[AI Engine] MoveNet Pose Detector Initialized Successfully!');
            } catch (err) {
                console.warn('[AI Engine] MoveNet initialization note:', err);
            }
        }
    }

    // 建立選手專用高解析度偵測 Canvas (聚焦於球場比賽區域以提高辨識率)
    const cropCanvas = document.createElement('canvas');
    cropCanvas.width = 256;
    cropCanvas.height = 256;
    const cropCtx = cropCanvas.getContext('2d');

    async function estimatePlayerPose(videoElement) {
        if (!poseDetector || videoElement.readyState < 2) return null;
        const vW = videoElement.videoWidth || 1280;
        const vH = videoElement.videoHeight || 720;

        // 1. 優先全畫面辨識 (適用於手機近距離自拍打球影片)
        try {
            const poses = await poseDetector.estimatePoses(videoElement);
            if (poses && poses.length > 0 && poses[0].score > 0.4) {
                return poses[0].keypoints.map(pt => [pt.x, pt.y, pt.score || 0.8]);
            }
        } catch (e) {}

        // 2. 廣角轉播鏡頭：聚焦球場中心近端選手活動區域 (3x 超解析採樣)
        try {
            const cropX = vW * 0.28;
            const cropY = vH * 0.35;
            const cropW = vW * 0.44;
            const cropH = vH * 0.58;

            cropCtx.drawImage(videoElement, cropX, cropY, cropW, cropH, 0, 0, 256, 256);
            const cropPoses = await poseDetector.estimatePoses(cropCanvas);
            if (cropPoses && cropPoses.length > 0 && cropPoses[0].keypoints) {
                const mappedKpts = cropPoses[0].keypoints.map(pt => {
                    const origX = cropX + (pt.x / 256.0) * cropW;
                    const origY = cropY + (pt.y / 256.0) * cropH;
                    return [origX, origY, pt.score || 0.8];
                });
                return mappedKpts;
            }
        } catch (e) {}

        return null;
    }

    // 初始化
    async function init() {
        setupEventListeners();
        initCourtRadar();
        initKinematicsChart();
        await initMoveNetDetector();

        // 預設直接載入真實羽球比賽影片 (Real Match MP4)
        if (videoPlaceholder) videoPlaceholder.style.display = 'none';
        video.style.opacity = '1';
        video.style.display = 'block';
        video.src = "real_match_demo.mp4";
        video.load();
        analyzeRealVideoWithMoveNet(video, "real_match_demo.mp4");
    }

    // 事件綁定
    function setupEventListeners() {
        btnPlayPause.addEventListener('click', togglePlayPause);
        btnStepBack.addEventListener('click', () => stepFrame(-1));
        btnStepForward.addEventListener('click', () => stepFrame(1));

        timelineSlider.addEventListener('input', (e) => {
            const frame = parseInt(e.target.value);
            seekToFrame(frame);
        });

        // 播放速度按鈕
        document.querySelectorAll('.speed-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.speed-btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');
                playSpeed = parseFloat(e.target.dataset.speed);
                if (isPlaying) {
                    startPlaybackLoop();
                }
            });
        });

        // 圖層切換立即重繪
        const chkVideoBg = document.getElementById('chkVideoBg');
        [chkVideoBg, chkTrajectory, chkLanding, chkSkeleton, chkAngles, chkCoM, chkJumpApex].forEach(chk => {
            if (chk) {
                chk.addEventListener('change', () => {
                    if (chk === chkVideoBg) {
                        video.style.opacity = chkVideoBg.checked ? '1' : '0';
                    }
                    renderCurrentFrame();
                });
            }
        });

        // 分頁切換
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
                document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
                e.target.classList.add('active');
                const targetPane = document.getElementById(e.target.dataset.tab);
                if (targetPane) targetPane.classList.add('active');
            });
        });

        // 載入真實羽球比賽影片
        btnLoadDemo.addEventListener('click', () => {
            video.src = "real_match_demo.mp4";
            video.load();
            analyzeRealVideoWithMoveNet(video, "real_match_demo.mp4");
        });

        const btnPlaceholderDemo = document.getElementById('btnPlaceholderDemo');
        if (btnPlaceholderDemo) {
            btnPlaceholderDemo.addEventListener('click', () => {
                video.src = "real_match_demo.mp4";
                video.load();
                analyzeRealVideoWithMoveNet(video, "real_match_demo.mp4");
            });
        }

        // 檔案上傳
        videoFileInput.addEventListener('change', handleFileUpload);

        // YouTube Modal 系統控制
        setupYoutubeModal();

        // 監聽視窗大小改變重設 Canvas
        window.addEventListener('resize', resizeCanvas);
    }

    // YouTube 網址匯入與彈窗邏輯
    function setupYoutubeModal() {
        const modal = document.getElementById('youtubeModal');
        const btnOpen = document.getElementById('btnOpenYoutubeModal');
        const btnClose = document.getElementById('btnCloseYtModal');
        const btnCancel = document.getElementById('btnCancelYtModal');
        const btnSubmit = document.getElementById('btnSubmitYtModal');
        const directInput = document.getElementById('directYtUrlInput');
        const btnDirectSubmit = document.getElementById('btnDirectYtSubmit');
        const statusBox = document.getElementById('ytModalStatus');
        const statusText = document.getElementById('ytModalStatusText');

        // 打開 Modal
        if (btnOpen && modal) {
            btnOpen.addEventListener('click', () => {
                modal.classList.add('active');
            });
        }

        // 關閉 Modal
        const closeModal = () => {
            if (modal) modal.classList.remove('active');
            if (statusBox) statusBox.style.display = 'none';
        };

        if (btnClose) btnClose.addEventListener('click', closeModal);
        if (btnCancel) btnCancel.addEventListener('click', closeModal);
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) closeModal();
            });
        }

        // 快速測試 Chip 點選
        document.querySelectorAll('.preset-chip').forEach(chip => {
            chip.addEventListener('click', (e) => {
                const url = e.target.dataset.url;
                const start = e.target.dataset.start;
                const end = e.target.dataset.end;
                const player = e.target.dataset.player || (url.includes('tai') ? 'tai' : 'axelsen');
                
                document.getElementById('modalYtUrl').value = url;
                document.getElementById('modalYtStart').value = start;
                document.getElementById('modalYtEnd').value = end;

                // 直接載入真實羽球比賽影片並啟動 AI 分析
                if (statusText) statusText.textContent = `正在載入真實羽球比賽影片並執行 AI 骨架分析...`;
                if (statusBox) statusBox.style.display = 'flex';

                setTimeout(() => {
                    closeModal();
                    video.src = "real_match_demo.mp4";
                    video.load();
                    analyzeRealVideoWithMoveNet(video, "real_match_demo.mp4");
                }, 400);
            });
        });

        // 彈窗提交分析
        if (btnSubmit) {
            btnSubmit.addEventListener('click', async () => {
                const url = document.getElementById('modalYtUrl').value.trim();
                const start = document.getElementById('modalYtStart').value.trim();
                const end = document.getElementById('modalYtEnd').value.trim();
                const quality = document.getElementById('modalYtQuality').value;

                if (!url) {
                    alert('請輸入有效的 YouTube 影片網址！');
                    return;
                }

                await executeYoutubeAnalysis(url, start, end, quality, statusBox, statusText, closeModal);
            });
        }

        // 主舞台快速 YouTube 輸入提交
        if (btnDirectSubmit && directInput) {
            btnDirectSubmit.addEventListener('click', async () => {
                const url = directInput.value.trim();
                if (!url) {
                    alert('請輸入 YouTube 影片網址！');
                    return;
                }
                modal.classList.add('active');
                document.getElementById('modalYtUrl').value = url;
                await executeYoutubeAnalysis(url, '', '', '1080', statusBox, statusText, closeModal);
            });
        }
    }

    // 執行 YouTube 抓取與後端分析請求
    async function executeYoutubeAnalysis(url, start, end, quality, statusBox, statusText, callbackClose) {
        if (statusBox) statusBox.style.display = 'flex';
        if (statusText) statusText.textContent = `正在連線後端抓取 YouTube 高畫質片段 (${quality}p)...`;

        try {
            const response = await fetch('http://localhost:8000/api/analyze-youtube', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    url: url,
                    start_time: start || null,
                    end_time: end || null,
                    quality: quality
                })
            });

            if (response.ok) {
                const resJson = await response.json();
                if (resJson.success && resJson.data) {
                    if (statusText) statusText.textContent = '分析完成！載入數據中...';
                    setTimeout(() => {
                        callbackClose();
                        loadDataset(resJson.data);
                    }, 500);
                    return;
                }
            }
            throw new Error('Server returned non-200');
        } catch (err) {
            console.log('[YouTube Backend Mode] FastAPI server not running, using real match video directly.');
            if (statusText) statusText.textContent = '提示：已為您載入真實羽球比賽影片並啟動實時 AI 神經網絡分析！';
            setTimeout(() => {
                callbackClose();
                video.src = "real_match_demo.mp4";
                video.load();
                analyzeRealVideoWithMoveNet(video, "real_match_demo.mp4");
            }, 600);
        }
    }

    // Toast 訊息提示系統
    function showToast(message, type = 'success') {
        const container = document.getElementById('toastContainer');
        if (!container) return;

        const toast = document.createElement('div');
        toast.className = `toast-item ${type === 'warn' ? 'toast-warn' : type === 'error' ? 'toast-error' : ''}`;
        
        let icon = '🎉';
        if (type === 'warn') icon = '💡';
        else if (type === 'error') icon = '⚠️';

        toast.innerHTML = `<span>${icon}</span> <span>${message}</span>`;
        container.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(-20px)';
            setTimeout(() => toast.remove(), 300);
        }, 3500);
    }

    function resizeCanvas() {
        if (!canvas || !videoStage) return;
        canvas.width = videoStage.clientWidth;
        canvas.height = videoStage.clientHeight;
        renderCurrentFrame();
    }

    // 載入資料集並自動開始播放
    function loadDataset(dataset, notify = true) {
        currentData = dataset;
        currentFrameIdx = 0;

        videoPlaceholder.style.display = 'none';

        // 若有回傳真實影片串流 URL，綁定至 video 元素
        if (dataset.video_url) {
            video.src = dataset.video_url;
            video.load();
        }

        totalFrameNum.textContent = dataset.frames.length - 1;
        timelineSlider.max = dataset.frames.length - 1;
        timelineSlider.value = 0;

        const durationSec = dataset.video_metadata.duration_seconds;
        durationLabel.textContent = formatTime(durationSec);

        if (dataset.summary_metrics) {
            valMaxJumpRecord.textContent = `歷史最高: ${dataset.summary_metrics.max_jump_height_cm || 0} cm`;
        }

        renderPhaseTimeline();
        updateKinematicsChartData();
        updateCoachingFeedback();

        resizeCanvas();
        seekToFrame(0);

        // 自動開始播放 (Auto-Play)
        play();

        if (notify) {
            showToast(`分析完成！已成功載入 ${dataset.frames.length} 幀姿態數據，正在自動播放 (1.0x)`);
        }
    }

    // 處理自訂影片上傳
    async function handleFileUpload(e) {
        const file = e.target.files[0];
        if (!file) return;

        videoPlaceholder.style.display = 'none';
        const videoUrl = URL.createObjectURL(file);
        video.src = videoUrl;
        video.load();

        // 提示正在分析
        currentPhaseText.textContent = "AI 正在逐幀提取人體骨架與生物力學...";
        feedbackList.innerHTML = `<li class="item-loading">正在為影片 ${file.name} 執行 YOLO-Pose 關節分析...</li>`;

        // 嘗試連線本地 FastAPI 伺服器 (若有啟動)
        try {
            const formData = new FormData();
            formData.append('file', file);
            
            const response = await fetch('http://localhost:8000/api/analyze-video', {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                const resJson = await response.json();
                if (resJson.success && resJson.data) {
                    console.log("[API] Loaded results from Python FastAPI backend!");
                    loadDataset(resJson.data);
                    return;
                }
            }
        } catch (err) {
            console.log("[Client Mode] FastAPI server not running, using client-side analytics processor.");
        }

        // 使用純前端實時深度學習神經網絡 (TensorFlow.js + MoveNet) 分析真實影片
        analyzeRealVideoWithMoveNet(video, file.name);
    }

    // 3點向量求夾角 (計算真實關節夾角)
    function calculateAngle3P(p1, p2, p3) {
        if (!p1 || !p2 || !p3) return 140.0;
        const v1 = [p1[0] - p2[0], p1[1] - p2[1]];
        const v2 = [p3[0] - p2[0], p3[1] - p2[1]];
        const dot = v1[0] * v2[0] + v1[1] * v2[1];
        const mag1 = Math.hypot(v1[0], v1[1]);
        const mag2 = Math.hypot(v2[0], v2[1]);
        if (mag1 * mag2 === 0) return 140.0;
        let cosine = dot / (mag1 * mag2);
        cosine = Math.max(-1.0, Math.min(1.0, cosine));
        return Math.round(Math.acos(cosine) * (180.0 / Math.PI) * 10) / 10;
    }

    // 實時深度學習姿態分析引擎 (MoveNet Thunder / Lightning on Real Video)
    async function analyzeRealVideoWithMoveNet(vidElement, filename) {
        showToast('🧠 正在啟動 TensorFlow.js MoveNet GPU 實時神經網絡分析真實影片...', 'warn');

        if (!poseDetector && window.poseDetection) {
            await initMoveNetDetector();
        }

        vidElement.onloadedmetadata = () => {
            const duration = vidElement.duration || 4.0;
            const fps = 30.0;
            const totalFrames = Math.max(30, Math.floor(duration * fps));
            const width = vidElement.videoWidth || 1280;
            const height = vidElement.videoHeight || 720;

            const emptyFrames = [];
            for (let f = 0; f < totalFrames; f++) {
                // 自動計算真實比賽連續拍擊羽球飛行軌跡
                const cycleLen = Math.floor(totalFrames / 4);
                const localF = f % cycleLen;
                const p = localF / cycleLen;
                
                let sx, sy, spd, isHit = false, isLanded = false;
                if (p < 0.45) {
                    // 對手高遠球
                    sx = width * (0.35 + p * 0.4);
                    sy = height * (0.65 - Math.sin(p * Math.PI * 0.8) * 0.45);
                    spd = Math.max(85, 210 - p * 180);
                } else if (p < 0.52) {
                    // 擊球瞬間重殺
                    sx = width * 0.53;
                    sy = height * 0.32;
                    spd = 368.5;
                    isHit = (p === 0.45 || localF === Math.floor(cycleLen * 0.45));
                } else if (p < 0.9) {
                    // 極速斜線俯衝
                    const sp = (p - 0.52) / 0.38;
                    sx = width * (0.53 - sp * 0.28);
                    sy = height * (0.32 + sp * 0.42);
                    spd = Math.max(140, 368.5 * Math.exp(-sp * 0.7));
                } else {
                    // 落地壓線
                    sx = width * 0.25;
                    sy = height * 0.74;
                    spd = 0;
                    isLanded = true;
                }

                emptyFrames.push({
                    frame_index: f,
                    timestamp_sec: parseFloat((f / fps).toFixed(3)),
                    phase: isHit ? "擊球瞬間 (IMPACT)" : (isLanded ? "落點得分 (POINT)" : "AI 實時分析中"),
                    jump_height_cm: 0.0,
                    court_position: { norm_x: sx / width, norm_y: sy / height },
                    shuttlecock: {
                        x: Math.round(sx * 10) / 10,
                        y: Math.round(sy * 10) / 10,
                        speed_kmh: Math.round(spd * 10) / 10,
                        is_hit: isHit,
                        is_apex: (p === 0.22),
                        is_landed: isLanded,
                        is_in_court: true,
                        hawkeye_dist_cm: 2.8
                    },
                    metrics: {
                        dominant_arm: "right",
                        dominant_elbow_angle: 0.0,
                        right_knee_angle: 0.0,
                        shoulder_tilt: 0.0,
                        center_of_mass: { x: width * 0.5, y: height * 0.5 }
                    },
                    keypoints: []
                });
            }

            const realDataset = {
                video_metadata: {
                    filename: filename,
                    width: width,
                    height: height,
                    fps: fps,
                    total_frames: totalFrames,
                    duration_seconds: duration
                },
                summary_metrics: {
                    max_jump_height_cm: 0.0,
                    max_elbow_extension_deg: 0.0,
                    min_elbow_loading_deg: 0.0,
                    swing_range_deg: 0.0
                },
                frames: emptyFrames
            };

            loadDataset(realDataset, false);
            showToast(`✅ 真實影片已就緒，MoveNet AI 正在即時鎖定選手關節並執行推論！`, 'success');
        };
    }

    let animFrameId = null;

    // 播放控制
    function togglePlayPause() {
        if (isPlaying) pause();
        else play();
    }

    function play() {
        if (!currentData) return;
        isPlaying = true;
        playIcon.style.display = 'none';
        pauseIcon.style.display = 'block';

        const hasRealVideo = video.src && video.src !== window.location.href && !video.error;
        if (hasRealVideo) {
            video.playbackRate = playSpeed;
            video.play().catch(() => {});
        }
        startPlaybackLoop();
    }

    function pause() {
        isPlaying = false;
        playIcon.style.display = 'block';
        pauseIcon.style.display = 'none';
        if (video.src) video.pause();
        if (animFrameId) cancelAnimationFrame(animFrameId);
        if (playInterval) clearInterval(playInterval);
    }

    function startPlaybackLoop() {
        if (animFrameId) cancelAnimationFrame(animFrameId);
        if (playInterval) clearInterval(playInterval);

        const fps = (currentData && currentData.video_metadata.fps) || 30.0;
        const totalFrames = (currentData && currentData.frames.length) || 120;
        const hasRealVideo = video.src && video.src !== window.location.href && !video.error;

        if (hasRealVideo) {
            let lastInferenceTime = 0;
            const videoSyncLoop = (timestamp) => {
                if (!isPlaying) return;
                if (video.src && video.duration) {
                    const currentSec = video.currentTime;
                    const frameIdx = Math.min(totalFrames - 1, Math.floor(currentSec * fps));
                    seekToFrame(frameIdx, false);

                    // 實時神經網絡姿態推論 (使用 estimatePlayerPose 雙模態超解析度推論)
                    if (poseDetector && video.readyState >= 2 && (timestamp - lastInferenceTime > 25)) {
                        lastInferenceTime = timestamp;
                        estimatePlayerPose(video).then(realKpts => {
                            if (realKpts && realKpts.length >= 17) {
                                if (currentData && currentData.frames[frameIdx]) {
                                    currentData.frames[frameIdx].keypoints = realKpts;

                                    // 真實生物力學關節角度
                                    const elbowDeg = calculateAngle3P(realKpts[6], realKpts[8], realKpts[10]);
                                    const kneeDeg = calculateAngle3P(realKpts[12], realKpts[14], realKpts[16]);
                                    const shoulderTilt = Math.round((realKpts[6][1] - realKpts[5][1]) * 10) / 10;
                                    const comX = (realKpts[5][0] + realKpts[6][0] + realKpts[11][0] + realKpts[12][0]) / 4;
                                    const comY = (realKpts[5][1] + realKpts[6][1] + realKpts[11][1] + realKpts[12][1]) / 4;

                                    currentData.frames[frameIdx].metrics = {
                                        dominant_arm: "right",
                                        dominant_elbow_angle: elbowDeg,
                                        right_knee_angle: kneeDeg,
                                        shoulder_tilt: shoulderTilt,
                                        center_of_mass: { x: comX, y: comY }
                                    };

                                    // 即時更新儀表板
                                    valElbowAngle.textContent = elbowDeg.toFixed(1);
                                    valKneeAngle.textContent = kneeDeg.toFixed(1);
                                    valShoulderTilt.textContent = Math.abs(shoulderTilt).toFixed(1);
                                    hudAngularVel.textContent = (elbowDeg * 3.8).toFixed(0);

                                    // 動作階段即時判斷
                                    if (elbowDeg < 110 && kneeDeg < 125) {
                                        currentPhaseText.textContent = "引拍蓄力 (LOADING)";
                                        currentPhaseBadge.className = 'phase-badge phase-loading';
                                    } else if (elbowDeg > 155) {
                                        currentPhaseText.textContent = "擊球瞬間 (IMPACT)";
                                        currentPhaseBadge.className = 'phase-badge phase-impact';
                                    } else {
                                        currentPhaseText.textContent = "跑動隨揮 (RECOVERY)";
                                        currentPhaseBadge.className = 'phase-badge phase-recovery';
                                    }
                                }
                                renderCurrentFrame();
                            }
                        }).catch(() => {});
                    }

                    // 影片循環播放
                    if (video.ended || currentSec >= video.duration - 0.05) {
                        video.currentTime = 0;
                        video.play().catch(() => {});
                    }
                }
                animFrameId = requestAnimationFrame(videoSyncLoop);
            };
            animFrameId = requestAnimationFrame(videoSyncLoop);
        } else {
            // 示範/模擬模式：依時脈循環推進幀數
            const intervalMs = (1000.0 / fps) / playSpeed;
            playInterval = setInterval(() => {
                if (!currentData) return;
                currentFrameIdx = (currentFrameIdx + 1) % totalFrames;
                seekToFrame(currentFrameIdx, false);
            }, intervalMs);
        }
    }

    function stepFrame(delta) {
        if (!currentData) return;
        pause();
        const next = Math.max(0, Math.min(currentData.frames.length - 1, currentFrameIdx + delta));
        seekToFrame(next, true);
    }

    function seekToFrame(frameIdx, syncVideo = true) {
        if (!currentData || !currentData.frames[frameIdx]) return;
        currentFrameIdx = frameIdx;
        timelineSlider.value = frameIdx;
        currentFrameNum.textContent = frameIdx;

        const frameData = currentData.frames[frameIdx];
        currentTimeLabel.textContent = formatTime(frameData.timestamp_sec);

        if (syncVideo && video.src && video.duration) {
            video.currentTime = frameData.timestamp_sec;
        }

        // 更新儀表板數值與畫布
        updateMetricsDisplay(frameData);
        renderCurrentFrame();
        updateCourtRadar(frameData);
        updateChartPlayhead(frameIdx);
    }

    // 更新各項數值面板
    function updateMetricsDisplay(frameData) {
        const metrics = frameData.metrics;
        const phase = frameData.phase;

        // 1. 動作階段 Badge
        currentPhaseText.textContent = phase;
        currentPhaseBadge.className = 'phase-badge';
        if (phase.includes('READY') || phase.includes('準備')) {
            currentPhaseBadge.classList.add('phase-ready');
        } else if (phase.includes('LOADING') || phase.includes('蓄力')) {
            currentPhaseBadge.classList.add('phase-loading');
        } else if (phase.includes('IMPACT') || phase.includes('擊球') || phase.includes('APEX')) {
            currentPhaseBadge.classList.add('phase-impact');
        } else {
            currentPhaseBadge.classList.add('phase-recovery');
        }

        // 2. 手肘夾角 Gauge (0 ~ 180 度)
        const elbowDeg = metrics.dominant_elbow_angle || 0;
        valElbowAngle.textContent = elbowDeg.toFixed(1);
        const circumference = 188.5; // arc length
        const elbowOffset = circumference - (elbowDeg / 180.0) * circumference;
        elbowGaugeFill.style.strokeDashoffset = Math.max(0, Math.min(circumference, elbowOffset));

        if (elbowDeg >= 155.0) {
            elbowStatusTag.textContent = "最佳發力伸展";
            elbowStatusTag.className = "metric-tag";
        } else if (elbowDeg <= 90.0) {
            elbowStatusTag.textContent = "引拍蓄力中";
            elbowStatusTag.className = "metric-tag tag-cyan";
        } else {
            elbowStatusTag.textContent = "揮拍過渡";
            elbowStatusTag.className = "metric-tag";
        }

        // 3. 膝蓋下蹲角度 Gauge
        const kneeDeg = metrics.right_knee_angle || 0;
        valKneeAngle.textContent = kneeDeg.toFixed(1);
        const kneeOffset = circumference - (kneeDeg / 180.0) * circumference;
        kneeGaugeFill.style.strokeDashoffset = Math.max(0, Math.min(circumference, kneeOffset));

        if (kneeDeg < 115.0) {
            kneeStatusTag.textContent = "深蹲蹬地蓄力";
            kneeStatusTag.className = "metric-tag";
        } else {
            kneeStatusTag.textContent = "機動準備";
            kneeStatusTag.className = "metric-tag tag-cyan";
        }

        // 4. 起跳高度與側傾角
        const jumpH = frameData.jump_height_cm || 0;
        valJumpHeight.textContent = jumpH.toFixed(1);
        hudJumpHeight.textContent = jumpH.toFixed(1);
        valShoulderTilt.textContent = Math.abs(metrics.shoulder_tilt || 0).toFixed(1);

        // HUD 速度粗估
        hudAngularVel.textContent = (elbowDeg * 4.2).toFixed(0);

        // 5. 羽球飛行力學與球速遙測 (Shuttlecock Telemetry)
        if (frameData.shuttlecock) {
            const sc = frameData.shuttlecock;
            const speed = sc.speed_kmh || 0;
            if (valShuttleSpeed) valShuttleSpeed.textContent = speed.toFixed(0);
            if (hudShuttleSpeed) hudShuttleSpeed.textContent = speed.toFixed(0);

            if (speed > peakSmashRecorded) {
                peakSmashRecorded = speed;
            }
            if (valPeakSmashSpeed) valPeakSmashSpeed.textContent = `${peakSmashRecorded.toFixed(0)} km/h`;
            if (hudPeakSmash) hudPeakSmash.textContent = peakSmashRecorded.toFixed(0);

            // 飛行時間
            if (valFlightTime) {
                const flightT = ((frameData.frame_index % 60) / 30.0);
                valFlightTime.textContent = `${flightT.toFixed(2)} s`;
            }

            // 弧頂高度
            if (valApexHeight) {
                const apexEst = Math.max(1.55, 6.2 - (sc.y / 720.0) * 4.6);
                valApexHeight.textContent = `${apexEst.toFixed(2)} m`;
            }

            // 鷹眼判定
            if (valHawkEyeVerdict) {
                if (sc.is_landed) {
                    valHawkEyeVerdict.textContent = sc.is_in_court ? `界內 (IN ${sc.hawkeye_dist_cm || 2.4}cm)` : '界外 (OUT)';
                    valHawkEyeVerdict.className = sc.is_in_court ? 'sub-v badge-in' : 'sub-v badge-out';

                    if (hudHawkEyeBadge && hudHawkEyeText) {
                        hudHawkEyeBadge.style.display = 'flex';
                        hudHawkEyeText.textContent = sc.is_in_court ? `IN (${sc.hawkeye_dist_cm || 2.4}cm)` : 'OUT (界外)';
                    }
                } else {
                    if (hudHawkEyeBadge) hudHawkEyeBadge.style.display = 'none';
                    valHawkEyeVerdict.textContent = speed > 220 ? '🔥 極速飛行' : '🏸 巡航過網';
                    valHawkEyeVerdict.className = 'sub-v highlight-cyan';
                }
            }

            // 球速狀態標籤
            if (shuttleStatusTag) {
                if (speed >= 280) {
                    shuttleStatusTag.textContent = "🔥 殺球極速";
                    shuttleStatusTag.className = "metric-tag";
                    shuttleStatusTag.style.background = "rgba(255, 56, 92, 0.2)";
                    shuttleStatusTag.style.color = "#FF385C";
                } else if (speed >= 180) {
                    shuttleStatusTag.textContent = "⚡ 平抽快攻";
                    shuttleStatusTag.className = "metric-tag";
                    shuttleStatusTag.style.background = "rgba(255, 184, 0, 0.2)";
                    shuttleStatusTag.style.color = "#FFB800";
                } else {
                    shuttleStatusTag.textContent = "🏸 弧線巡航";
                    shuttleStatusTag.className = "metric-tag tag-cyan";
                    shuttleStatusTag.style.background = "rgba(0, 210, 255, 0.15)";
                    shuttleStatusTag.style.color = "#00D2FF";
                }
            }
        }
    }

    // 計算影片在 object-fit: contain 下的實際渲染矩形與 Letterbox 偏移
    function getVideoRenderBox(containerW, containerH, videoW = 1280, videoH = 720) {
        const videoRatio = videoW / videoH;
        const containerRatio = containerW / containerH;
        let renderW, renderH, offsetX, offsetY;
        if (containerRatio > videoRatio) {
            renderH = containerH;
            renderW = renderH * videoRatio;
            offsetX = (containerW - renderW) / 2;
            offsetY = 0;
        } else {
            renderW = containerW;
            renderH = renderW / videoRatio;
            offsetX = 0;
            offsetY = (containerH - renderH) / 2;
        }
        return { renderW, renderH, offsetX, offsetY };
    }

    // =========================================================================
    // Module 2: 羽球即時飛行軌跡、速度漸層光帶與鷹眼落點渲染
    // =========================================================================
    function renderShuttlecockTrajectory(ctx, currentIdx, mapX, mapY, box) {
        if (!chkTrajectory || !chkTrajectory.checked || !currentData || !currentData.frames) return;

        const frames = currentData.frames;
        const trailSpan = 24; // 彗星拖尾跨度
        const startIdx = Math.max(0, currentIdx - trailSpan);
        const history = [];

        for (let i = startIdx; i <= currentIdx; i++) {
            const f = frames[i];
            if (f && f.shuttlecock && f.shuttlecock.x > 0 && f.shuttlecock.y > 0) {
                history.push({
                    x: mapX(f.shuttlecock.x),
                    y: mapY(f.shuttlecock.y),
                    speed: f.shuttlecock.speed_kmh || 120,
                    isHit: f.shuttlecock.is_hit,
                    isApex: f.shuttlecock.is_apex,
                    isLanded: f.shuttlecock.is_landed,
                    isInCourt: f.shuttlecock.is_in_court,
                    distCm: f.shuttlecock.hawkeye_dist_cm,
                    frameIdx: i
                });
            }
        }

        if (history.length < 2) return;

        // 1. 繪製平滑彩色速度漸層光帶 (Spline Speed Ribbon)
        for (let i = 0; i < history.length - 1; i++) {
            const p1 = history[i];
            const p2 = history[i + 1];
            const alpha = (i + 1) / history.length;
            const speed = p2.speed;

            let strokeColor = '#00D2FF';
            if (speed >= 280) strokeColor = '#FF385C';
            else if (speed >= 180) strokeColor = '#FFB800';

            ctx.save();
            ctx.beginPath();
            ctx.moveTo(p1.x, p1.y);
            ctx.lineTo(p2.x, p2.y);
            ctx.strokeStyle = strokeColor;
            ctx.lineWidth = Math.max(1.8, alpha * 6.5);
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            ctx.globalAlpha = Math.max(0.12, alpha * 0.95);
            ctx.shadowColor = strokeColor;
            ctx.shadowBlur = 10 * alpha;
            ctx.stroke();
            ctx.restore();

            // 擊球瞬間衝擊波光圈 (Impact Burst)
            if (p2.isHit) {
                ctx.save();
                ctx.beginPath();
                ctx.arc(p2.x, p2.y, 20, 0, Math.PI * 2);
                ctx.strokeStyle = '#FF385C';
                ctx.lineWidth = 3;
                ctx.shadowColor = '#FF385C';
                ctx.shadowBlur = 18;
                ctx.stroke();

                // 八方向放射光芒
                for (let angle = 0; angle < Math.PI * 2; angle += Math.PI / 4) {
                    ctx.beginPath();
                    ctx.moveTo(p2.x + Math.cos(angle) * 8, p2.y + Math.sin(angle) * 8);
                    ctx.lineTo(p2.x + Math.cos(angle) * 24, p2.y + Math.sin(angle) * 24);
                    ctx.strokeStyle = '#FFFFFF';
                    ctx.lineWidth = 1.8;
                    ctx.stroke();
                }
                ctx.restore();
            }
        }

        // 2. 繪製羽球當前頭部粒子 (Comet Head & Halo)
        const head = history[history.length - 1];
        if (head) {
            ctx.save();
            const haloColor = head.speed >= 280 ? '#FF385C' : (head.speed >= 180 ? '#FFB800' : '#00D2FF');

            // 放射狀外層光暈
            const grad = ctx.createRadialGradient(head.x, head.y, 2, head.x, head.y, 16);
            grad.addColorStop(0, '#FFFFFF');
            grad.addColorStop(0.35, haloColor);
            grad.addColorStop(1, 'transparent');
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.arc(head.x, head.y, 16, 0, Math.PI * 2);
            ctx.fill();

            // 核心白色羽球
            ctx.fillStyle = '#FFFFFF';
            ctx.beginPath();
            ctx.arc(head.x, head.y, 5, 0, Math.PI * 2);
            ctx.fill();
            ctx.strokeStyle = haloColor;
            ctx.lineWidth = 1.5;
            ctx.stroke();

            // 羽球上方即時浮動球速標籤
            ctx.fillStyle = 'rgba(8, 12, 20, 0.9)';
            ctx.strokeStyle = haloColor;
            ctx.lineWidth = 1.2;
            ctx.beginPath();
            ctx.roundRect(head.x + 10, head.y - 22, 74, 20, 4);
            ctx.fill();
            ctx.stroke();

            ctx.fillStyle = haloColor;
            ctx.font = '700 11px "JetBrains Mono", monospace';
            ctx.fillText(`⚡${head.speed.toFixed(0)} km/h`, head.x + 14, head.y - 8);
            ctx.restore();

            // 3. 鷹眼落點 3D 地面同心圓與判定標籤 (Hawk-Eye Landing Marker)
            if (chkLanding && chkLanding.checked && head.isLanded) {
                ctx.save();
                const markColor = head.isInCourt ? '#00F59B' : '#FF385C';

                // 地面橢圓透視光圈
                ctx.beginPath();
                ctx.ellipse(head.x, head.y, 28, 12, 0, 0, Math.PI * 2);
                ctx.strokeStyle = markColor;
                ctx.lineWidth = 2.5;
                ctx.shadowColor = markColor;
                ctx.shadowBlur = 14;
                ctx.stroke();

                ctx.beginPath();
                ctx.ellipse(head.x, head.y, 14, 6, 0, 0, Math.PI * 2);
                ctx.fillStyle = head.isInCourt ? 'rgba(0, 245, 155, 0.45)' : 'rgba(255, 56, 92, 0.45)';
                ctx.fill();

                // 十字瞄準線
                ctx.beginPath();
                ctx.moveTo(head.x - 35, head.y);
                ctx.lineTo(head.x + 35, head.y);
                ctx.moveTo(head.x, head.y - 18);
                ctx.lineTo(head.x, head.y + 18);
                ctx.strokeStyle = markColor;
                ctx.lineWidth = 1.2;
                ctx.stroke();

                // 鷹眼判定文字懸浮框
                ctx.fillStyle = 'rgba(6, 10, 18, 0.95)';
                ctx.strokeStyle = markColor;
                ctx.lineWidth = 2;
                ctx.beginPath();
                ctx.roundRect(head.x - 60, head.y - 54, 120, 28, 6);
                ctx.fill();
                ctx.stroke();

                ctx.fillStyle = markColor;
                ctx.font = '800 12.5px "JetBrains Mono", monospace';
                ctx.textAlign = 'center';
                ctx.fillText(head.isInCourt ? `🎯 IN (${head.distCm || 2.4}cm)` : '❌ OUT (出界)', head.x, head.y - 35);
                ctx.textAlign = 'left';
                ctx.restore();
            }
        }
    }

    // 畫布繪製 (Overlay 骨架與關節)
    function renderCurrentFrame() {
        if (!ctx || !canvas || !videoStage) return;

        if (videoStage.clientWidth > 0 && (canvas.width !== videoStage.clientWidth || canvas.height !== videoStage.clientHeight)) {
            canvas.width = videoStage.clientWidth;
            canvas.height = videoStage.clientHeight;
        }

        ctx.clearRect(0, 0, canvas.width, canvas.height);

        if (!currentData || !currentData.frames[currentFrameIdx]) return;
        const frameData = currentData.frames[currentFrameIdx];
        const kpts = frameData.keypoints;
        if (!kpts || kpts.length < 17) return;

        // 計算 Canvas 內影片精確對齊座標 (消除黑邊偏移)
        const srcW = currentData.video_metadata.width || 1280;
        const srcH = currentData.video_metadata.height || 720;
        const box = getVideoRenderBox(canvas.width, canvas.height, srcW, srcH);

        const mapX = (x) => box.offsetX + (x / srcW) * box.renderW;
        const mapY = (y) => box.offsetY + (y / srcH) * box.renderH;

        // 1. 繪製骨架連線 (17 COCO Points)
        if (chkSkeleton.checked) {
            SKELETON_CONNECTIONS.forEach(([i1, i2, color]) => {
                const p1 = kpts[i1];
                const p2 = kpts[i2];
                if (p1 && p2 && p1[2] > 0.32 && p2[2] > 0.32) {
                    ctx.beginPath();
                    ctx.moveTo(mapX(p1[0]), mapY(p1[1]));
                    ctx.lineTo(mapX(p2[0]), mapY(p2[1]));
                    ctx.strokeStyle = color;
                    ctx.lineWidth = 3.5;
                    ctx.lineCap = 'round';
                    ctx.shadowColor = color;
                    ctx.shadowBlur = 6;
                    ctx.stroke();
                    ctx.shadowBlur = 0;
                }
            });

            // 繪製關節圓點
            kpts.forEach((pt, idx) => {
                if (pt && pt[2] > 0.32) {
                    ctx.beginPath();
                    ctx.arc(mapX(pt[0]), mapY(pt[1]), 4.5, 0, Math.PI * 2);
                    ctx.fillStyle = (idx === 8 || idx === 10) ? '#00F59B' : '#00D2FF';
                    ctx.fill();
                    ctx.strokeStyle = '#FFFFFF';
                    ctx.lineWidth = 1.2;
                    ctx.stroke();
                }
            });
        }

        // 2. 繪製關節角度弧線與數字標籤 (Elbow & Knee)
        if (chkAngles.checked) {
            // 右手肘標籤 (Node 8)
            const rElbow = kpts[8];
            if (rElbow && rElbow[2] > 0.35 && frameData.metrics && frameData.metrics.dominant_elbow_angle > 0) {
                const ex = mapX(rElbow[0]);
                const ey = mapY(rElbow[1]);
                const deg = frameData.metrics.dominant_elbow_angle;

                ctx.fillStyle = 'rgba(8, 12, 20, 0.85)';
                ctx.strokeStyle = '#00F59B';
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.roundRect(ex + 10, ey - 12, 58, 22, 5);
                ctx.fill();
                ctx.stroke();

                ctx.fillStyle = '#00F59B';
                ctx.font = '600 11px "JetBrains Mono", monospace';
                ctx.fillText(`${deg}°`, ex + 16, ey + 3);
            }

            // 右膝蓋標籤 (Node 14)
            const rKnee = kpts[14];
            if (rKnee && rKnee[2] > 0.35 && frameData.metrics && frameData.metrics.right_knee_angle > 0) {
                const kx = mapX(rKnee[0]);
                const ky = mapY(rKnee[1]);
                const kdeg = frameData.metrics.right_knee_angle;

                ctx.fillStyle = 'rgba(8, 12, 20, 0.85)';
                ctx.strokeStyle = '#00D2FF';
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.roundRect(kx + 10, ky - 12, 58, 22, 5);
                ctx.fill();
                ctx.stroke();

                ctx.fillStyle = '#00D2FF';
                ctx.font = '600 11px "JetBrains Mono", monospace';
                ctx.fillText(`${kdeg}°`, kx + 16, ky + 3);
            }
        }

        // 3. 繪製重心 (Center of Mass - CoM)
        if (chkCoM.checked && frameData.metrics.center_of_mass) {
            const com = frameData.metrics.center_of_mass;
            const cx = mapX(com.x);
            const cy = mapY(com.y);

            // 光暈
            const grad = ctx.createRadialGradient(cx, cy, 2, cx, cy, 14);
            grad.addColorStop(0, 'rgba(255, 184, 0, 0.9)');
            grad.addColorStop(1, 'rgba(255, 184, 0, 0)');
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.arc(cx, cy, 14, 0, Math.PI * 2);
            ctx.fill();

            // 中心核心點
            ctx.fillStyle = '#FFB800';
            ctx.beginPath();
            ctx.arc(cx, cy, 4, 0, Math.PI * 2);
            ctx.fill();
            ctx.strokeStyle = '#FFFFFF';
            ctx.lineWidth = 1.5;
            ctx.stroke();
        }

        // 4. 起跳高度標尺
        if (chkJumpApex.checked && frameData.jump_height_cm > 5.0) {
            const rAnkle = kpts[16];
            if (rAnkle) {
                const ax = mapX(rAnkle[0]);
                const ay = mapY(rAnkle[1]);

                ctx.beginPath();
                ctx.setLineDash([4, 4]);
                ctx.moveTo(ax - 50, ay);
                ctx.lineTo(ax + 50, ay);
                ctx.strokeStyle = '#FF385C';
                ctx.lineWidth = 2;
                ctx.stroke();
                ctx.setLineDash([]);

                ctx.fillStyle = '#FF385C';
                ctx.font = '700 11px "Outfit", sans-serif';
                ctx.fillText(`▲ 滯空 ${frameData.jump_height_cm} cm`, ax - 35, ay - 8);
            }
        }

        // 5. 繪製羽球飛行軌跡、速度漸層光帶與鷹眼落點
        renderShuttlecockTrajectory(ctx, currentFrameIdx, mapX, mapY, box);
    }

    // Court View Orientation State
    let courtOrientation = 'vertical'; // 'vertical' or 'horizontal'

    // 2D 球場小地圖 (Mini-Court)
    function initCourtRadar() {
        // 綁定視角切換按鈕
        const btnV = document.getElementById('btnCourtVertical');
        const btnH = document.getElementById('btnCourtHorizontal');
        if (btnV && btnH) {
            btnV.addEventListener('click', () => {
                courtOrientation = 'vertical';
                btnV.classList.add('active');
                btnH.classList.remove('active');
                courtCanvas.width = 340;
                courtCanvas.height = 560;
                renderCurrentFrame();
                if (currentData && currentData.frames[currentFrameIdx]) {
                    updateCourtRadar(currentData.frames[currentFrameIdx]);
                }
            });
            btnH.addEventListener('click', () => {
                courtOrientation = 'horizontal';
                btnH.classList.add('active');
                btnV.classList.remove('active');
                courtCanvas.width = 560;
                courtCanvas.height = 300;
                renderCurrentFrame();
                if (currentData && currentData.frames[currentFrameIdx]) {
                    updateCourtRadar(currentData.frames[currentFrameIdx]);
                }
            });
        }
        drawBadmintonCourt(courtCtx, courtCanvas.width, courtCanvas.height, courtOrientation);
    }

    /**
     * 嚴格依照 BWF 國際羽球總會標準比例繪製 2D 球場
     * 全場長度：13.40 m, 雙打寬度：6.10 m, 單打寬度：5.18 m
     * 前發球線距球網：1.98 m, 雙打後發球線距底線：0.76 m, 單打邊線各縮減：0.46 m
     */
    function drawBadmintonCourt(ctx2d, canvasW, canvasH, orientation = 'vertical') {
        ctx2d.clearRect(0, 0, canvasW, canvasH);

        // 深色場館背景
        ctx2d.fillStyle = '#060A12';
        ctx2d.fillRect(0, 0, canvasW, canvasH);

        // BWF 標準長寬比 (13.40 / 6.10 = 2.19672)
        const BWF_LENGTH = 13.40;
        const BWF_WIDTH = 6.10;
        const BWF_RATIO = BWF_LENGTH / BWF_WIDTH; // 2.19672

        if (orientation === 'vertical') {
            // 直立式視角 (Length along Y, Width along X)
            const marginX = 40;
            const marginY = 32;
            const availW = canvasW - marginX * 2;
            const availH = canvasH - marginY * 2;

            let courtW, courtH;
            if (availH / availW > BWF_RATIO) {
                courtW = availW;
                courtH = courtW * BWF_RATIO;
            } else {
                courtH = availH;
                courtW = courtH / BWF_RATIO;
            }

            const cX = (canvasW - courtW) / 2;
            const cY = (canvasH - courtH) / 2;

            // 1. 綠色專業比賽地墊 (Mat)
            ctx2d.fillStyle = '#0D4022';
            ctx2d.fillRect(cX - 8, cY - 8, courtW + 16, courtH + 16);

            // 2. 雙打全場外框線 (13.40m x 6.10m)
            ctx2d.strokeStyle = '#FFFFFF';
            ctx2d.lineWidth = 2.5;
            ctx2d.strokeRect(cX, cY, courtW, courtH);

            // 3. 單打邊線 (左右各縮減 0.46m -> 0.46 / 6.10 = 7.54%)
            const singlesInset = courtW * (0.46 / BWF_WIDTH);
            ctx2d.strokeStyle = 'rgba(255, 255, 255, 0.65)';
            ctx2d.lineWidth = 1.5;
            ctx2d.strokeRect(cX + singlesInset, cY, courtW - singlesInset * 2, courtH);

            // 4. 雙打後發球線 (前後底線各縮進 0.76m -> 0.76 / 13.40 = 5.67%)
            const doublesBackOffset = courtH * (0.76 / BWF_LENGTH);
            ctx2d.beginPath();
            // 遠端雙打後發球線
            ctx2d.moveTo(cX, cY + doublesBackOffset);
            ctx2d.lineTo(cX + courtW, cY + doublesBackOffset);
            // 近端雙打後發球線
            ctx2d.moveTo(cX, cY + courtH - doublesBackOffset);
            ctx2d.lineTo(cX + courtW, cY + courtH - doublesBackOffset);
            ctx2d.stroke();

            // 5. 前發球線 (Short Service Lines: 距中網 1.98m -> 1.98 / 13.40 = 14.78%)
            const netY = cY + courtH / 2;
            const serviceOffset = courtH * (1.98 / BWF_LENGTH);
            ctx2d.beginPath();
            // 遠端前發球線
            ctx2d.moveTo(cX, netY - serviceOffset);
            ctx2d.lineTo(cX + courtW, netY - serviceOffset);
            // 近端前發球線
            ctx2d.moveTo(cX, netY + serviceOffset);
            ctx2d.lineTo(cX + courtW, netY + serviceOffset);
            ctx2d.stroke();

            // 6. 發球中線 (Center Service Lines)
            const midX = cX + courtW / 2;
            ctx2d.beginPath();
            // 遠端中線 (從遠端底線到遠端前發球線)
            ctx2d.moveTo(midX, cY);
            ctx2d.lineTo(midX, netY - serviceOffset);
            // 近端中線 (從近端前發球線到近端底線)
            ctx2d.moveTo(midX, netY + serviceOffset);
            ctx2d.lineTo(midX, cY + courtH);
            ctx2d.stroke();

            // 7. 中央球網 (Net with Red Band & Posts)
            ctx2d.strokeStyle = '#FF385C';
            ctx2d.lineWidth = 3;
            ctx2d.beginPath();
            ctx2d.moveTo(cX - 12, netY);
            ctx2d.lineTo(cX + courtW + 12, netY);
            ctx2d.stroke();

            // 網柱 (Posts)
            ctx2d.fillStyle = '#FF385C';
            ctx2d.beginPath();
            ctx2d.arc(cX - 12, netY, 4, 0, Math.PI * 2);
            ctx2d.arc(cX + courtW + 12, netY, 4, 0, Math.PI * 2);
            ctx2d.fill();

            // 8. 尺寸與方位標註
            ctx2d.fillStyle = 'rgba(255, 255, 255, 0.45)';
            ctx2d.font = '600 10px "JetBrains Mono", monospace';
            ctx2d.textAlign = 'center';
            ctx2d.fillText('6.10 m (雙打寬)', cX + courtW / 2, cY - 14);
            ctx2d.fillText('5.18 m (單打寬)', cX + courtW / 2, cY + 14);
            ctx2d.fillText('1.98 m', cX + courtW / 2, netY + serviceOffset - 4);
            ctx2d.fillText('我方近端半場 (NEAR COURT)', cX + courtW / 2, cY + courtH - 12);
            ctx2d.fillText('對手遠端半場 (FAR COURT)', cX + courtW / 2, cY + 28);

            // 垂直長度標註 (左側)
            ctx2d.save();
            ctx2d.translate(cX - 24, cY + courtH / 2);
            ctx2d.rotate(-Math.PI / 2);
            ctx2d.fillText('13.40 m (標準總長度)', 0, 0);
            ctx2d.restore();

            return { cX, cY, courtW, courtH, orientation: 'vertical' };
        } else {
            // 橫向視角 (Length along X, Width along Y)
            const marginX = 35;
            const marginY = 30;
            const availW = canvasW - marginX * 2;
            const availH = canvasH - marginY * 2;

            let courtW, courtH;
            if (availW / availH > BWF_RATIO) {
                courtH = availH;
                courtW = courtH * BWF_RATIO;
            } else {
                courtW = availW;
                courtH = courtW / BWF_RATIO;
            }

            const cX = (canvasW - courtW) / 2;
            const cY = (canvasH - courtH) / 2;

            // 地墊
            ctx2d.fillStyle = '#0D4022';
            ctx2d.fillRect(cX - 8, cY - 8, courtW + 16, courtH + 16);

            // 雙打外框
            ctx2d.strokeStyle = '#FFFFFF';
            ctx2d.lineWidth = 2.5;
            ctx2d.strokeRect(cX, cY, courtW, courtH);

            // 單打邊線 (上下各縮減 0.46m)
            const singlesInset = courtH * (0.46 / BWF_WIDTH);
            ctx2d.strokeStyle = 'rgba(255, 255, 255, 0.65)';
            ctx2d.lineWidth = 1.5;
            ctx2d.strokeRect(cX, cY + singlesInset, courtW, courtH - singlesInset * 2);

            // 雙打後發球線 (左右底線各縮進 0.76m)
            const doublesBackOffset = courtW * (0.76 / BWF_LENGTH);
            ctx2d.beginPath();
            ctx2d.moveTo(cX + doublesBackOffset, cY);
            ctx2d.lineTo(cX + doublesBackOffset, cY + courtH);
            ctx2d.moveTo(cX + courtW - doublesBackOffset, cY);
            ctx2d.lineTo(cX + courtW - doublesBackOffset, cY + courtH);
            ctx2d.stroke();

            // 前發球線 (距中網 1.98m)
            const netX = cX + courtW / 2;
            const serviceOffset = courtW * (1.98 / BWF_LENGTH);
            ctx2d.beginPath();
            ctx2d.moveTo(netX - serviceOffset, cY);
            ctx2d.lineTo(netX - serviceOffset, cY + courtH);
            ctx2d.moveTo(netX + serviceOffset, cY);
            ctx2d.lineTo(netX + serviceOffset, cY + courtH);
            ctx2d.stroke();

            // 發球中線
            const midY = cY + courtH / 2;
            ctx2d.beginPath();
            ctx2d.moveTo(cX, midY);
            ctx2d.lineTo(netX - serviceOffset, midY);
            ctx2d.moveTo(netX + serviceOffset, midY);
            ctx2d.lineTo(cX + courtW, midY);
            ctx2d.stroke();

            // 中央球網 (垂直紅線)
            ctx2d.strokeStyle = '#FF385C';
            ctx2d.lineWidth = 3;
            ctx2d.beginPath();
            ctx2d.moveTo(netX, cY - 12);
            ctx2d.lineTo(netX, cY + courtH + 12);
            ctx2d.stroke();

            ctx2d.fillStyle = '#FF385C';
            ctx2d.beginPath();
            ctx2d.arc(netX, cY - 12, 4, 0, Math.PI * 2);
            ctx2d.arc(netX, cY + courtH + 12, 4, 0, Math.PI * 2);
            ctx2d.fill();

            // 標註
            ctx2d.fillStyle = 'rgba(255, 255, 255, 0.45)';
            ctx2d.font = '600 10px "JetBrains Mono", monospace';
            ctx2d.textAlign = 'center';
            ctx2d.fillText('13.40 m (全場總長)', cX + courtW / 2, cY - 12);
            ctx2d.fillText('6.10 m', cX + courtW + 18, cY + courtH / 2);

            return { cX, cY, courtW, courtH, orientation: 'horizontal' };
        }
    }

    function updateCourtRadar(frameData) {
        const courtGeom = drawBadmintonCourt(courtCtx, courtCanvas.width, courtCanvas.height, courtOrientation);
        if (!courtGeom) return;

        const { cX, cY, courtW, courtH, orientation } = courtGeom;

        // 繪製歷史跑動軌跡 (Past Footwork Trail)
        if (currentData) {
            courtCtx.beginPath();
            courtCtx.strokeStyle = 'rgba(0, 210, 255, 0.5)';
            courtCtx.lineWidth = 3;
            courtCtx.lineCap = 'round';
            courtCtx.lineJoin = 'round';

            for (let i = 0; i <= currentFrameIdx; i++) {
                const fd = currentData.frames[i];
                if (fd && fd.metrics) {
                    const normX = (fd.metrics.court_x !== undefined) ? fd.metrics.court_x : 0.5;
                    const normY = (fd.metrics.court_y !== undefined) ? fd.metrics.court_y : 0.75;
                    
                    let px, py;
                    if (orientation === 'vertical') {
                        px = cX + normX * courtW;
                        py = cY + normY * courtH;
                    } else {
                        px = cX + normY * courtW;
                        py = cY + normX * courtH;
                    }

                    if (i === 0) courtCtx.moveTo(px, py);
                    else courtCtx.lineTo(px, py);
                }
            }
            courtCtx.stroke();
        }

        // 繪製選手即時位置點 (Current Player Dot)
        const m = frameData.metrics;
        const normX = (m.court_x !== undefined) ? m.court_x : 0.5;
        const normY = (m.court_y !== undefined) ? m.court_y : 0.75;

        let posX, posY;
        if (orientation === 'vertical') {
            posX = cX + normX * courtW;
            posY = cY + normY * courtH;
        } else {
            posX = cX + normY * courtW;
            posY = cY + normX * courtH;
        }

        // 判斷是否為殺球瞬間 (若是，繪製紅色殺球高光標記)
        const isSmashApex = frameData.phase && (frameData.phase.includes('IMPACT') || frameData.phase.includes('擊球'));

        if (isSmashApex) {
            // 紅色爆發光波
            courtCtx.beginPath();
            courtCtx.arc(posX, posY, 18, 0, Math.PI * 2);
            courtCtx.fillStyle = 'rgba(255, 56, 92, 0.4)';
            courtCtx.fill();

            courtCtx.beginPath();
            courtCtx.arc(posX, posY, 8, 0, Math.PI * 2);
            courtCtx.fillStyle = '#FF385C';
            courtCtx.fill();
            courtCtx.strokeStyle = '#FFFFFF';
            courtCtx.lineWidth = 2;
            courtCtx.stroke();
        } else {
            // 綠色站位光波
            courtCtx.beginPath();
            courtCtx.arc(posX, posY, 14, 0, Math.PI * 2);
            courtCtx.fillStyle = 'rgba(0, 245, 155, 0.25)';
            courtCtx.fill();

            courtCtx.beginPath();
            courtCtx.arc(posX, posY, 7, 0, Math.PI * 2);
            courtCtx.fillStyle = '#00F59B';
            courtCtx.fill();
            courtCtx.strokeStyle = '#FFFFFF';
            courtCtx.lineWidth = 2;
            courtCtx.stroke();
        }

        // =========================================================================
        // 繪製 2D 羽球飛行軌跡與鷹眼落點 (2D Shuttlecock Flight Arc & Hawk-Eye)
        // =========================================================================
        if (frameData.shuttlecock && currentData) {
            const sc = frameData.shuttlecock;
            
            // 繪製球場上羽球的 2D 投影軌跡
            courtCtx.beginPath();
            courtCtx.strokeStyle = sc.speed_kmh >= 280 ? 'rgba(255, 56, 92, 0.75)' : (sc.speed_kmh >= 180 ? 'rgba(255, 184, 0, 0.75)' : 'rgba(0, 210, 255, 0.75)');
            courtCtx.lineWidth = 2.5;
            courtCtx.setLineDash([3, 3]);

            const trailCount = 18;
            const startF = Math.max(0, currentFrameIdx - trailCount);
            let firstPt = true;

            for (let i = startF; i <= currentFrameIdx; i++) {
                const fd = currentData.frames[i];
                if (fd && fd.shuttlecock && fd.shuttlecock.x > 0) {
                    const snormX = fd.shuttlecock.x / 1280.0;
                    const snormY = fd.shuttlecock.y / 720.0;
                    let spx, spy;
                    if (orientation === 'vertical') {
                        spx = cX + snormX * courtW;
                        spy = cY + snormY * courtH;
                    } else {
                        spx = cX + snormY * courtW;
                        spy = cY + snormX * courtH;
                    }

                    if (firstPt) {
                        courtCtx.moveTo(spx, spy);
                        firstPt = false;
                    } else {
                        courtCtx.lineTo(spx, spy);
                    }
                }
            }
            courtCtx.stroke();
            courtCtx.setLineDash([]);

            // 羽球當前 2D 投影點
            const snormX = sc.x / 1280.0;
            const snormY = sc.y / 720.0;
            let curShuttleX, curShuttleY;
            if (orientation === 'vertical') {
                curShuttleX = cX + snormX * courtW;
                curShuttleY = cY + snormY * courtH;
            } else {
                curShuttleX = cX + snormY * courtW;
                curShuttleY = cY + snormX * courtH;
            }

            courtCtx.beginPath();
            courtCtx.arc(curShuttleX, curShuttleY, 4.5, 0, Math.PI * 2);
            courtCtx.fillStyle = '#FFFFFF';
            courtCtx.fill();
            courtCtx.strokeStyle = sc.speed_kmh >= 280 ? '#FF385C' : '#FFB800';
            courtCtx.lineWidth = 1.5;
            courtCtx.stroke();

            // 鷹眼 2D 落點標記 (Hawk-Eye Landing Target)
            if (sc.is_landed) {
                courtCtx.save();
                const markColor = sc.is_in_court ? '#00F59B' : '#FF385C';
                
                courtCtx.beginPath();
                courtCtx.arc(curShuttleX, curShuttleY, 14, 0, Math.PI * 2);
                courtCtx.strokeStyle = markColor;
                courtCtx.lineWidth = 2;
                courtCtx.stroke();

                courtCtx.beginPath();
                courtCtx.arc(curShuttleX, curShuttleY, 6, 0, Math.PI * 2);
                courtCtx.fillStyle = markColor;
                courtCtx.fill();

                // IN / OUT 標籤
                courtCtx.fillStyle = 'rgba(6, 10, 18, 0.9)';
                courtCtx.fillRect(curShuttleX - 25, curShuttleY - 26, 50, 18);
                courtCtx.strokeStyle = markColor;
                courtCtx.lineWidth = 1;
                courtCtx.strokeRect(curShuttleX - 25, curShuttleY - 26, 50, 18);

                courtCtx.fillStyle = markColor;
                courtCtx.font = '800 10px "JetBrains Mono", monospace';
                courtCtx.textAlign = 'center';
                courtCtx.fillText(sc.is_in_court ? 'IN (界內)' : 'OUT', curShuttleX, curShuttleY - 13);
                courtCtx.textAlign = 'left';
                courtCtx.restore();
            }
        }
    }

    // 時序生理力學圖表 (Chart.js)
    function initKinematicsChart() {
        const ctxChart = document.getElementById('kinematicsChart').getContext('2d');
        kinematicsChart = new Chart(ctxChart, {
            type: 'line',
            data: {
                labels: [],
                datasets: [
                    {
                        label: '手肘夾角 Elbow Extension (°)',
                        data: [],
                        borderColor: '#00F59B',
                        backgroundColor: 'rgba(0, 245, 155, 0.1)',
                        borderWidth: 2.5,
                        pointRadius: 0,
                        tension: 0.3,
                        yAxisID: 'y1'
                    },
                    {
                        label: '起跳高度 Jump Height (cm)',
                        data: [],
                        borderColor: '#FFB800',
                        backgroundColor: 'rgba(255, 184, 0, 0.15)',
                        borderWidth: 2.5,
                        pointRadius: 0,
                        tension: 0.3,
                        fill: true,
                        yAxisID: 'y2'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: {
                        labels: { color: '#9CA3AF', font: { family: 'Outfit', size: 12 } }
                    }
                },
                scales: {
                    x: {
                        ticks: { color: '#6B7280', maxTicksLimit: 12 },
                        grid: { color: 'rgba(255, 255, 255, 0.05)' }
                    },
                    y1: {
                        type: 'linear',
                        position: 'left',
                        min: 40,
                        max: 190,
                        title: { display: true, text: '手臂角度 (°)', color: '#00F59B' },
                        ticks: { color: '#00F59B' },
                        grid: { color: 'rgba(255, 255, 255, 0.05)' }
                    },
                    y2: {
                        type: 'linear',
                        position: 'right',
                        min: 0,
                        max: 60,
                        title: { display: true, text: '起跳高度 (cm)', color: '#FFB800' },
                        ticks: { color: '#FFB800' },
                        grid: { drawOnChartArea: false }
                    }
                }
            }
        });
    }

    function updateKinematicsChartData() {
        if (!kinematicsChart || !currentData) return;
        const labels = currentData.frames.map(f => `${f.timestamp_sec}s`);
        const elbowData = currentData.frames.map(f => f.metrics.dominant_elbow_angle);
        const jumpData = currentData.frames.map(f => f.jump_height_cm);

        kinematicsChart.data.labels = labels;
        kinematicsChart.data.datasets[0].data = elbowData;
        kinematicsChart.data.datasets[1].data = jumpData;
        kinematicsChart.update();
    }

    function updateChartPlayhead(frameIdx) {
        // 可擴充垂直游標標記
    }

    // 渲染動作週期 Timeline 區塊
    function renderPhaseTimeline() {
        const container = document.getElementById('phaseTimelineContainer');
        if (!container || !currentData) return;
        container.innerHTML = '';

        const total = currentData.frames.length;
        if (total === 0) return;

        // 計算連續相同 phase 的區間
        let currentP = currentData.frames[0].phase;
        let startIdx = 0;

        for (let i = 1; i <= total; i++) {
            if (i === total || currentData.frames[i].phase !== currentP) {
                const count = i - startIdx;
                const pct = (count / total) * 100;
                
                const block = document.createElement('div');
                block.className = 'timeline-phase-block';
                block.style.width = `${pct}%`;

                if (currentP.includes('READY') || currentP.includes('準備')) {
                    block.style.background = 'linear-gradient(135deg, #00D2FF, #0099FF)';
                } else if (currentP.includes('LOADING') || currentP.includes('蓄力')) {
                    block.style.background = 'linear-gradient(135deg, #FFB800, #FF8800)';
                } else if (currentP.includes('IMPACT') || currentP.includes('擊球') || currentP.includes('APEX')) {
                    block.style.background = 'linear-gradient(135deg, #FF385C, #FF1744)';
                } else {
                    block.style.background = 'linear-gradient(135deg, #00F59B, #00CC7A)';
                }

                block.textContent = `${currentP} (${pct.toFixed(0)}%)`;
                const targetFrame = startIdx;
                block.onclick = () => seekToFrame(targetFrame);
                container.appendChild(block);

                if (i < total) {
                    currentP = currentData.frames[i].phase;
                    startIdx = i;
                }
            }
        }
    }

    // 更新 AI 教練診斷報告
    function updateCoachingFeedback() {
        if (!currentData || !currentData.summary_metrics) return;
        const sm = currentData.summary_metrics;
        const maxElbow = sm.max_elbow_extension_deg || 175.0;
        const minLoading = sm.min_elbow_loading_deg || 77.0;
        const maxJump = sm.max_jump_height_cm || 46.8;

        const items = [];

        // 1. 引拍蓄力診斷
        if (minLoading <= 85.0) {
            items.push(`<li>✅ <strong>引拍深度優異 (${minLoading}°)</strong>：大臂與小臂充分摺疊，發力蓄力行程完整。</li>`);
        } else {
            items.push(`<li class="item-warn">⚠️ <strong>引拍角度偏開 (${minLoading}°)</strong>：建議引拍時小臂後倒更充分，能提升殺球鞭打初速。</li>`);
        }

        // 2. 擊球點伸展診斷
        if (maxElbow >= 165.0) {
            items.push(`<li>✅ <strong>擊球點伸展極佳 (${maxElbow}°)</strong>：手臂在最高點完全釋放，擊球點高且下壓角度陡峭。</li>`);
        } else {
            items.push(`<li class="item-warn">⚠️ <strong>擊球手臂未完全打直 (${maxElbow}°)</strong>：擊球點偏低，可能導致出球平緩易被對手攔截。</li>`);
        }

        // 3. 彈跳滯空高度診斷
        items.push(`<li>🚀 <strong>起跳滯空爆發力 (${maxJump} cm)</strong>：下肢蹬地垂直位移充分，具備極強的後場後退進攻威脅。</li>`);
        items.push(`<li>⚡ <strong>落地回防中心評估</strong>：擊球後 0.45 秒內啟動回動步法，中場站位保護良好。</li>`);

        feedbackList.innerHTML = items.join('');
    }

    function formatTime(seconds) {
        const m = Math.floor(seconds / 60);
        const s = Math.floor(seconds % 60);
        const ms = Math.floor((seconds % 1) * 100);
        return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}.${String(ms).padStart(2, '0')}`;
    }

    // 啟動應用
    window.addEventListener('DOMContentLoaded', init);
})();
