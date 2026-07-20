// Beyblade Telemetry Live Dashboard Simulation Engine (Enhanced Physics)
document.addEventListener("DOMContentLoaded", () => {

    // DOM Elements
    const simBtn = document.getElementById("simBtn");
    const resetBtn = document.getElementById("resetBtn");
    const clearLogBtn = document.getElementById("clearLogBtn");
    const eventLog = document.getElementById("eventLog");

    const p1RpmText = document.getElementById("p1RpmText");
    const p2RpmText = document.getElementById("p2RpmText");
    const p1RpmBar = document.getElementById("p1RpmBar");
    const p2RpmBar = document.getElementById("p2RpmBar");
    const p1RpmVal = document.getElementById("p1RpmVal");
    const p2RpmVal = document.getElementById("p2RpmVal");
    const p1GVal = document.getElementById("p1GVal");
    const p2GVal = document.getElementById("p2GVal");

    const maxShootVal = document.getElementById("maxShootVal");
    const maxGVal = document.getElementById("maxGVal");
    const tiltAngleVal = document.getElementById("tiltAngleVal");

    // Canvas & 2D Context
    const canvas = document.getElementById("arenaCanvas");
    const ctx = canvas.getContext("2d");
    const width = canvas.width;
    const height = canvas.height;
    const centerX = width / 2;
    const centerY = height / 2;

    // Simulation State
    let isRunning = false;
    let animId = null;
    let startTime = 0;
    let elapsedSec = 0;
    let maxGRecorded = 1.0;
    let maxShootRecorded = 0;

    // Particles array for collision sparks
    let particles = [];

    // Beyblades Enhanced Physics State
    let p1 = {
        id: "p1",
        name: "DranSword (Attack)",
        color: "#00f3ff",
        x: centerX + 70,
        y: centerY,
        vx: -1.5,
        vy: 2.8,
        angle: 0,
        radius: 130,
        rpm: 0,
        maxRpm: 8850,
        baseDecay: 12, // Natural air resistance RPM drop per sec
        gForce: 1.0,
        tilt: 1.5,
        mass: 35.0, // grams
        isDashing: false,
        cooldownDash: 0
    };

    let p2 = {
        id: "p2",
        name: "WizardRod (Stamina)",
        color: "#ff3366",
        x: centerX - 40,
        y: centerY,
        vx: 1.0,
        vy: -1.2,
        angle: Math.PI,
        radius: 40,
        rpm: 0,
        maxRpm: 8100,
        baseDecay: 6,
        gForce: 1.0,
        tilt: 0.6,
        mass: 38.0,
        isDashing: false,
        cooldownDash: 0
    };

    // Chart.js Setup
    const chartCtx = document.getElementById("gForceChart").getContext("2d");
    const gChart = new Chart(chartCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'P1 DranSword (g)',
                    borderColor: '#00f3ff',
                    backgroundColor: 'rgba(0, 243, 255, 0.1)',
                    data: [],
                    borderWidth: 2,
                    tension: 0.2,
                    pointRadius: 0
                },
                {
                    label: 'P2 WizardRod (g)',
                    borderColor: '#ff3366',
                    backgroundColor: 'rgba(255, 51, 102, 0.1)',
                    data: [],
                    borderWidth: 2,
                    tension: 0.2,
                    pointRadius: 0
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            scales: {
                x: {
                    display: true,
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#8b949e', font: { family: 'JetBrains Mono', size: 10 } }
                },
                y: {
                    min: 0,
                    max: 200,
                    grid: { color: 'rgba(255, 255, 255, 0.08)' },
                    ticks: { color: '#8b949e', font: { family: 'JetBrains Mono', size: 10 } },
                    title: { display: true, text: 'G-Force (g)', color: '#8b949e' }
                }
            },
            plugins: {
                legend: { labels: { color: '#f0f6fc', font: { family: 'Outfit', size: 12 } } }
            }
        }
    });

    // Helper: Add Log
    function addLog(msg, type = "system") {
        const item = document.createElement("div");
        item.className = `log-item ${type}`;
        const timeStr = elapsedSec.toFixed(1);
        item.innerHTML = `<span class="time">[${timeStr.padStart(4, '0')}s]</span> <span class="msg">${msg}</span>`;
        eventLog.appendChild(item);
        eventLog.scrollTop = eventLog.scrollHeight;
    }

    // Spark Particles for Collisions
    function createSparks(x, y, count = 12) {
        for (let i = 0; i < count; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 2 + Math.random() * 5;
            particles.push({
                x: x,
                y: y,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                life: 1.0,
                decay: 0.05 + Math.random() * 0.05,
                color: Math.random() > 0.5 ? "#ffbe0b" : "#00f3ff"
            });
        }
    }

    function updateParticles() {
        for (let i = particles.length - 1; i >= 0; i--) {
            let p = particles[i];
            p.x += p.vx;
            p.y += p.vy;
            p.life -= p.decay;
            if (p.life <= 0) {
                particles.splice(i, 1);
            }
        }
    }

    function drawParticles() {
        particles.forEach(p => {
            ctx.save();
            ctx.globalAlpha = p.life;
            ctx.fillStyle = p.color;
            ctx.beginPath();
            ctx.arc(p.x, p.y, 2.5, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
        });
    }

    // Draw Arena & Coils
    function drawArena() {
        ctx.clearRect(0, 0, width, height);

        // Outer Bowl Boundary
        ctx.save();
        ctx.beginPath();
        ctx.arc(centerX, centerY, 210, 0, Math.PI * 2);
        ctx.fillStyle = "#060911";
        ctx.fill();
        ctx.lineWidth = 4;
        ctx.strokeStyle = "rgba(0, 243, 255, 0.2)";
        ctx.stroke();

        // Zone 3: Extreme Rail (Outer Ø32cm -> R=180) - Glowing Accent
        ctx.beginPath();
        ctx.arc(centerX, centerY, 180, 0, Math.PI * 2);
        ctx.strokeStyle = "rgba(255, 51, 102, 0.35)";
        ctx.lineWidth = 14;
        ctx.stroke();

        // Draw gear teeth ticks on Extreme Rail
        ctx.strokeStyle = "rgba(255, 190, 11, 0.6)";
        ctx.lineWidth = 2;
        for (let a = 0; a < Math.PI * 2; a += Math.PI / 24) {
            let x1 = centerX + Math.cos(a) * 173;
            let y1 = centerY + Math.sin(a) * 173;
            let x2 = centerX + Math.cos(a) * 187;
            let y2 = centerY + Math.sin(a) * 187;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.stroke();
        }

        // Zone 2: Tornado Ridge (Ø22cm -> R=120)
        ctx.beginPath();
        ctx.arc(centerX, centerY, 120, 0, Math.PI * 2);
        ctx.strokeStyle = "rgba(168, 85, 247, 0.25)";
        ctx.lineWidth = 10;
        ctx.stroke();

        // Zone 1: Center Bowl (Ø12cm -> R=60)
        ctx.beginPath();
        ctx.arc(centerX, centerY, 60, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(0, 243, 255, 0.05)";
        ctx.fill();
        ctx.strokeStyle = "rgba(0, 243, 255, 0.3)";
        ctx.lineWidth = 2;
        ctx.stroke();

        // Center Point
        ctx.beginPath();
        ctx.arc(centerX, centerY, 4, 0, Math.PI * 2);
        ctx.fillStyle = "#00f3ff";
        ctx.fill();

        ctx.restore();
    }

    // Draw Spinning Tops
    function drawTop(top) {
        if (top.rpm <= 0) return;

        ctx.save();
        ctx.translate(top.x, top.y);

        // Dash Glow Effect
        if (top.isDashing) {
            ctx.beginPath();
            ctx.arc(0, 0, 24, 0, Math.PI * 2);
            ctx.fillStyle = "#ffbe0b";
            ctx.globalAlpha = 0.5;
            ctx.fill();
        }

        // Outer aura
        ctx.beginPath();
        ctx.arc(0, 0, 16, 0, Math.PI * 2);
        ctx.fillStyle = top.color;
        ctx.globalAlpha = 0.3;
        ctx.fill();

        // Core top
        ctx.globalAlpha = 1.0;
        ctx.beginPath();
        ctx.arc(0, 0, 11, 0, Math.PI * 2);
        ctx.fillStyle = top.color;
        ctx.fill();
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.stroke();

        // Rotation Marker Line
        ctx.rotate(top.angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(9, 0);
        ctx.strokeStyle = "#000000";
        ctx.lineWidth = 3;
        ctx.stroke();

        ctx.restore();
    }

    // Physics Update Loop
    function updatePhysics() {
        [p1, p2].forEach(top => {
            if (top.rpm <= 0) return;

            // Rotation angle spin
            top.angle += (top.rpm / 60) * 0.016 * Math.PI * 2;

            // Center gravity pull towards center of stadium bowl
            let dx = centerX - top.x;
            let dy = centerY - top.y;
            let dist = Math.hypot(dx, dy);
            let gravityForce = 0.08 * (dist / 200);

            top.vx += (dx / (dist || 1)) * gravityForce;
            top.vy += (dy / (dist || 1)) * gravityForce;

            // Cooldowns
            if (top.cooldownDash > 0) top.cooldownDash--;

            // ZONE 3 EXTREME RAIL ACCELERATION BOOST (齒輪軌咬合加速區)
            if (dist >= 168 && dist <= 192 && top.rpm > 1500 && top.cooldownDash === 0) {
                top.isDashing = true;
                top.cooldownDash = 180; // 3 sec cooldown

                // Tangential acceleration vector
                let tangentAngle = Math.atan2(dy, dx) + Math.PI / 2;
                let boostSpeed = 4.5;
                top.vx = Math.cos(tangentAngle) * boostSpeed;
                top.vy = Math.sin(tangentAngle) * boostSpeed;

                // RPM Surge from gear engagement!
                let rpmSurge = 350 + Math.floor(Math.random() * 250);
                top.rpm = Math.min(top.maxRpm, top.rpm + rpmSurge);

                top.gForce += 15.0;

                addLog(`⚡ ${top.name} 咬合 Zone 3 齒輪軌！觸發 X-Dash 衝刺加速 (+${rpmSurge} RPM 瞬激)!`, "dash");
            } else {
                top.isDashing = false;
            }

            // Move top
            top.x += top.vx;
            top.y += top.vy;

            // Wall bounce at outer edge (R=205)
            let currentR = Math.hypot(top.x - centerX, top.y - centerY);
            if (currentR > 200) {
                let angle = Math.atan2(top.y - centerY, top.x - centerX);
                top.x = centerX + Math.cos(angle) * 200;
                top.y = centerY + Math.sin(angle) * 200;
                top.vx *= -0.7;
                top.vy *= -0.7;

                // Wall impact RPM loss
                top.rpm = Math.max(0, top.rpm - 120);
                top.gForce += 25.0;
            }

            // Friction & Air Decay
            // Tilt increases friction when RPM gets low
            top.tilt = (1.0 - (top.rpm / top.maxRpm)) * 15.0 + Math.random() * 0.5;
            let dynamicFriction = top.baseDecay + (top.tilt * 2.5);
            top.rpm = Math.max(0, top.rpm - dynamicFriction * 0.016 * 10);
            
            // Dampen linear velocity
            top.vx *= 0.985;
            top.vy *= 0.985;
        });

        // COLLISION SIMULATION & IMPULSE DECAY (碰撞減速與離心衝擊)
        let cDx = p1.x - p2.x;
        let cDy = p1.y - p2.y;
        let cDist = Math.hypot(cDx, cDy);

        if (cDist < 26 && p1.rpm > 0 && p2.rpm > 0) {
            // Overlap separation
            let overlap = 26 - cDist;
            let nx = cDx / (cDist || 1);
            let ny = cDy / (cDist || 1);

            p1.x += nx * overlap * 0.5;
            p1.y += ny * overlap * 0.5;
            p2.x -= nx * overlap * 0.5;
            p2.y -= ny * overlap * 0.5;

            // Elastic Impulse Calculation
            let kx = p1.vx - p2.vx;
            let ky = p1.vy - p2.vy;
            let p = 2 * (nx * kx + ny * ky) / (p1.mass + p2.mass);

            p1.vx -= p * p2.mass * nx * 1.5;
            p1.vy -= p * p2.mass * ny * 1.5;
            p2.vx += p * p1.mass * nx * 1.5;
            p2.vy += p * p1.mass * ny * 1.5;

            // Impact G-force peak (ADXL375 ±200g)
            let relativeSpeed = Math.hypot(kx, ky);
            let hitG = Math.min(200, Math.max(85, 75 + relativeSpeed * 35 + Math.random() * 45));

            p1.gForce = hitG;
            p2.gForce = hitG * 0.88;

            // COLLISION SPIN DECELERATION (碰撞導致角動量扣減衰減)
            let p1RpmLoss = Math.floor(150 + hitG * 1.8 + Math.random() * 100);
            let p2RpmLoss = Math.floor(120 + hitG * 1.5 + Math.random() * 80);

            p1.rpm = Math.max(0, p1.rpm - p1RpmLoss);
            p2.rpm = Math.max(0, p2.rpm - p2RpmLoss);

            // Record Max G
            if (hitG > maxGRecorded) {
                maxGRecorded = hitG;
                maxGVal.textContent = maxGRecorded.toFixed(1) + " g";
            }

            // Create collision spark particles
            createSparks((p1.x + p2.x) / 2, (p1.y + p2.y) / 2, 16);

            addLog(`💥 強烈對撞擊中！峰值 G 力: ${hitG.toFixed(1)}g (P1 -${p1RpmLoss} RPM / P2 -${p2RpmLoss} RPM)`, "hit");
        }
    }

    // Main Simulation Loop
    function updateSim() {
        if (!isRunning) return;

        const now = Date.now();
        elapsedSec = (now - startTime) / 1000;

        // Reset base G-force decay towards 1.0g
        p1.gForce = Math.max(1.0, p1.gForce * 0.88);
        p2.gForce = Math.max(1.0, p2.gForce * 0.88);

        updatePhysics();
        updateParticles();

        // Update UI
        p1RpmText.textContent = Math.round(p1.rpm);
        p2RpmText.textContent = Math.round(p2.rpm);
        p1RpmVal.textContent = Math.round(p1.rpm);
        p2RpmVal.textContent = Math.round(p2.rpm);
        p1GVal.textContent = p1.gForce.toFixed(1);
        p2GVal.textContent = p2.gForce.toFixed(1);

        p1RpmBar.style.width = Math.min(100, (p1.rpm / p1.maxRpm) * 100) + "%";
        p2RpmBar.style.width = Math.min(100, (p2.rpm / p2.maxRpm) * 100) + "%";

        tiltAngleVal.textContent = p1.tilt.toFixed(1) + "°";

        // Update Chart
        if (gChart.data.labels.length > 35) {
            gChart.data.labels.shift();
            gChart.data.datasets[0].data.shift();
            gChart.data.datasets[1].data.shift();
        }
        gChart.data.labels.push(elapsedSec.toFixed(1) + "s");
        gChart.data.datasets[0].data.push(p1.gForce);
        gChart.data.datasets[1].data.push(p2.gForce);
        gChart.update('none');

        // Draw Canvas Frame
        drawArena();
        drawParticles();
        drawTop(p1);
        drawTop(p2);

        // Check End Battle
        if (p1.rpm <= 0 && p2.rpm <= 0) {
            stopSimulation();
            addLog("🏁 對戰結束！雙方陀螺動能耗盡停轉，WPT 底座持續維持供電。", "system");
            return;
        }

        animId = requestAnimationFrame(updateSim);
    }

    function startSimulation() {
        if (isRunning) return;
        isRunning = true;
        startTime = Date.now();
        simBtn.textContent = "⏸ 暫停對戰";
        simBtn.classList.remove("primary-btn");
        simBtn.classList.add("secondary-btn");

        p1.rpm = p1.maxRpm;
        p2.rpm = p2.maxRpm;
        p1.vx = -1.5;
        p1.vy = 2.8;
        p2.vx = 1.0;
        p2.vy = -1.2;

        maxShootRecorded = Math.max(p1.maxRpm, p2.maxRpm);
        maxShootVal.textContent = maxShootRecorded + " RPM";

        addLog("🚀 雙方發射入場！WPT 對戰盤發射線圈開始全頻率電磁耦合...", "system");
        updateSim();
    }

    function stopSimulation() {
        isRunning = false;
        if (animId) cancelAnimationFrame(animId);
        simBtn.textContent = "▶ 啟動模擬對戰 (Live Battle)";
        simBtn.classList.remove("secondary-btn");
        simBtn.classList.add("primary-btn");
    }

    function resetSimulation() {
        stopSimulation();
        p1.rpm = 0;
        p2.rpm = 0;
        p1.x = centerX + 70;
        p1.y = centerY;
        p2.x = centerX - 40;
        p2.y = centerY;
        p1.vx = 0;
        p1.vy = 0;
        p2.vx = 0;
        p2.vy = 0;
        maxGRecorded = 1.0;
        particles = [];

        p1RpmText.textContent = "0";
        p2RpmText.textContent = "0";
        p1RpmBar.style.width = "0%";
        p2RpmBar.style.width = "0%";
        p1RpmVal.textContent = "0";
        p2RpmVal.textContent = "0";
        p1GVal.textContent = "1.0";
        p2GVal.textContent = "1.0";
        maxGVal.textContent = "0.0 g";
        maxShootVal.textContent = "0 RPM";
        tiltAngleVal.textContent = "0.0°";

        gChart.data.labels = [];
        gChart.data.datasets[0].data = [];
        gChart.data.datasets[1].data = [];
        gChart.update();

        eventLog.innerHTML = `<div class="log-item system"><span class="time">[00:00.0]</span><span class="msg">對戰台 WPT 無線感應供電運作中... 物理模擬核心已重置。</span></div>`;
        drawArena();
        drawTop(p1);
        drawTop(p2);
    }

    // Event Listeners
    simBtn.addEventListener("click", () => {
        if (isRunning) {
            stopSimulation();
        } else {
            startSimulation();
        }
    });

    resetBtn.addEventListener("click", resetSimulation);
    clearLogBtn.addEventListener("click", () => {
        eventLog.innerHTML = "";
    });

    // Initial render
    drawArena();
    drawTop(p1);
    drawTop(p2);
});
