// ===============================================================================
// Dynamometer HMI Pro (V2.2 - Modern + AutoTest, Safety Guard & S1/S2/S6 Duty Cycle)
// ===============================================================================

// Global System State
let isRunning = false;
let isLocked = false;
let manualTimerId = null;
let testStartTime = null;
let testRecords = [];

// Moving Average & Physics State
const movingAvgWindow = 5;
let torqueBuffer = [];
let simState = { speedRpm: 0, torqueNm: 0, temp: 25.0 };

// 🛡️ Safety Guard State
let baselineTorque = null;
let lastControlOutput = 0.0;
let isSafetyAlarm = false;

// 📈 T-N Auto Test State
let tnTestSequence = [];
let tnCurrentStepIdx = 0;
let tnTimerId = null;
let tnDwellTimerId = null;
let tnDwellRemaining = 0;
let tnRecordedPoints = [];

// 🗺️ Efficiency Map Scan State
let effGridSequence = [];
let effCurrentStepIdx = 0;
let effTimerId = null;
let effDwellTimerId = null;
let effDwellRemaining = 0;
let effMatrixData = {};

// ⏱️ S1 / S2 / S6 Duty Cycle Test State
let currentDutyMode = 'S1'; // 'S1', 'S2', 'S6'
let dutyIsRunning = false;
let dutyTimerId = null;
let dutySecondTimerId = null;
let dutyElapsedSec = 0;
let dutyTotalSec = 0;
let dutyCurrentCycle = 1;
let dutyTotalCycles = 5;
let dutyPhase = 'load'; // 'load' or 'idle'
let dutyPhaseElapsed = 0;
let dutyRecords = [];
let tempHistoryForEquil = []; // [ { timeSec, temp } ]

// ===============================================================================
// TAB SWITCHING LOGIC
// ===============================================================================
document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        
        btn.classList.add('active');
        const targetId = btn.getAttribute('data-tab');
        document.getElementById(targetId).classList.add('active');

        // Resize charts on tab activate
        if (targetId === 'tab-tn-auto' && chartTnResult) chartTnResult.resize();
        if (targetId === 'tab-eff-map') drawEfficiencyMapHeatmap();
        if (targetId === 'tab-duty-cycle') {
            if (chartDutyWaveform) chartDutyWaveform.resize();
            if (chartDutyTemp) chartDutyTemp.resize();
        }
    });
});

// Update System Clock
setInterval(() => {
    const now = new Date();
    document.getElementById('currentTime').textContent = now.toTimeString().split(' ')[0];
}, 1000);

// ===============================================================================
// 🛡️ SAFETY GUARD & BASELINE SNAPSHOT
// ===============================================================================
const btnSetBaseline = document.getElementById('btnSetBaseline');
const baseTorqueDisp = document.getElementById('baseTorqueDisp');
const valBaseTorqueDisp = document.getElementById('valBaseTorqueDisp');
const safetyWarningBanner = document.getElementById('safetyWarningBanner');
const safetyStatusBadge = document.getElementById('safetyStatusBadge');
const safetyDot = document.getElementById('safetyDot');
const safetyStatusText = document.getElementById('safetyStatusText');
const btnDismissWarning = document.getElementById('btnDismissWarning');

btnSetBaseline.addEventListener('click', () => {
    const currentTrq = parseFloat(document.getElementById('valTorque').textContent) || 0.0;
    baselineTorque = currentTrq;
    baseTorqueDisp.value = baselineTorque.toFixed(2);
    valBaseTorqueDisp.textContent = baselineTorque.toFixed(2) + ' Nm';
    hideSafetyAlarm();
});

btnDismissWarning.addEventListener('click', () => {
    hideSafetyAlarm();
});

function triggerSafetyAlarm(currentVal, baseVal, deltaPct, threshPct) {
    if (isSafetyAlarm) return;
    isSafetyAlarm = true;
    safetyWarningBanner.classList.remove('hidden');
    safetyStatusBadge.classList.add('warning');
    safetyDot.className = 'dot danger';
    safetyStatusText.textContent = `⚠️ 轉矩突變警示 (${deltaPct.toFixed(1)}% > ${threshPct}%)`;
    
    document.getElementById('bannerTitle').textContent = `🚨 安全警示：轉矩偏差超過 ${threshPct}% 門檻！`;
    document.getElementById('bannerDesc').textContent = 
        `實測轉矩: ${currentVal.toFixed(2)} Nm，基準: ${baseVal.toFixed(2)} Nm (偏移 ${deltaPct.toFixed(1)}%)。已啟動自動控制限幅防護！`;
}

function hideSafetyAlarm() {
    isSafetyAlarm = false;
    safetyWarningBanner.classList.add('hidden');
    safetyStatusBadge.classList.remove('warning');
    safetyDot.className = 'dot safe';
    safetyStatusText.textContent = '閉迴路安全防護：正常';
}

function applyControlRateLimiter(targetOutput, maxDelta) {
    const diff = targetOutput - lastControlOutput;
    let clamped = targetOutput;
    if (Math.abs(diff) > maxDelta) {
        clamped = lastControlOutput + Math.sign(diff) * maxDelta;
    }
    lastControlOutput = clamped;
    return clamped;
}

// ===============================================================================
// MANUAL REALTIME MONITORING & CHARTS
// ===============================================================================
const sliderSpeed = document.getElementById('sliderSpeed');
const sliderTorque = document.getElementById('sliderTorque');
const targetRpmDisp = document.getElementById('targetRpmDisp');
const targetTorqueDisp = document.getElementById('targetTorqueDisp');

sliderSpeed.addEventListener('input', (e) => {
    targetRpmDisp.textContent = e.target.value;
    document.getElementById('valSpeedSet').textContent = e.target.value;
});
sliderTorque.addEventListener('input', (e) => {
    targetTorqueDisp.textContent = e.target.value;
});

// Chart 1: Realtime T-N
const maxChartPoints = 40;
const labels = [], dataTorque = [], dataSpeed = [], dataTemp = [], dataPower = [];

const ctxTN = document.getElementById('chartTN').getContext('2d');
const chartTN = new Chart(ctxTN, {
    type: 'line',
    data: {
        labels: labels,
        datasets: [
            { label: '轉矩 (Nm)', borderColor: '#ff9900', backgroundColor: 'rgba(255, 153, 0, 0.1)', borderWidth: 2, pointRadius: 0, yAxisID: 'yTorque', data: dataTorque, tension: 0.3 },
            { label: '轉速 (rpm)', borderColor: '#00f2fe', backgroundColor: 'rgba(0, 242, 254, 0.05)', borderWidth: 1.5, pointRadius: 0, yAxisID: 'ySpeed', data: dataSpeed, tension: 0.3 }
        ]
    },
    options: {
        responsive: true, maintainAspectRatio: false, animation: false,
        plugins: { legend: { display: false } },
        scales: {
            x: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#64748b', font: { size: 10 } } },
            yTorque: { type: 'linear', position: 'left', grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#ff9900', font: { size: 10 } } },
            ySpeed: { type: 'linear', position: 'right', grid: { drawOnChartArea: false }, ticks: { color: '#00f2fe', font: { size: 10 } } }
        }
    }
});

// Chart 2: Temp & Power
const ctxTemp = document.getElementById('chartTemp').getContext('2d');
const chartTemp = new Chart(ctxTemp, {
    type: 'line',
    data: {
        labels: labels,
        datasets: [
            { label: 'CH1 溫度 (°C)', borderColor: '#ff3366', backgroundColor: 'rgba(255, 51, 102, 0.1)', borderWidth: 2, pointRadius: 0, yAxisID: 'yTemp', data: dataTemp, tension: 0.3 },
            { label: '機械功率 (kW)', borderColor: '#00f076', backgroundColor: 'rgba(0, 240, 118, 0.05)', borderWidth: 1.5, pointRadius: 0, yAxisID: 'yPower', data: dataPower, tension: 0.3 }
        ]
    },
    options: {
        responsive: true, maintainAspectRatio: false, animation: false,
        plugins: { legend: { display: false } },
        scales: {
            x: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#64748b', font: { size: 10 } } },
            yTemp: { type: 'linear', position: 'left', grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#ff3366', font: { size: 10 } } },
            yPower: { type: 'linear', position: 'right', grid: { drawOnChartArea: false }, ticks: { color: '#00f076', font: { size: 10 } } }
        }
    }
});

function calculateAndTick(targetRpm, targetTorque) {
    const threshPct = parseFloat(document.getElementById('safetyThreshPct').value) || 10.0;
    const maxDeltaNm = parseFloat(document.getElementById('maxDeltaNm').value) || 2.0;
    const chkAutoClamp = document.getElementById('chkAutoClamp').checked;

    let effectiveTargetTorque = targetTorque;
    if (chkAutoClamp) {
        effectiveTargetTorque = applyControlRateLimiter(targetTorque, maxDeltaNm);
    }

    simState.speedRpm += (targetRpm - simState.speedRpm) * 0.18 + (Math.random() - 0.5) * 2.5;
    
    if (!isLocked) {
        simState.torqueNm += (effectiveTargetTorque - simState.torqueNm) * 0.15 + (Math.random() - 0.5) * 3.0;
        const rawTorque = Math.max(0, simState.torqueNm);
        torqueBuffer.push(rawTorque);
        if (torqueBuffer.length > movingAvgWindow) torqueBuffer.shift();
    }
    const currentTorque = torqueBuffer.reduce((a, b) => a + b, 0) / torqueBuffer.length;
    const currentSpeed = Math.max(0, simState.speedRpm);

    // Safety Guard Check
    if (baselineTorque !== null && baselineTorque > 1.0) {
        const delta = Math.abs(currentTorque - baselineTorque);
        const deltaPct = (delta / baselineTorque) * 100.0;
        if (deltaPct > threshPct) {
            triggerSafetyAlarm(currentTorque, baselineTorque, deltaPct, threshPct);
        } else if (isSafetyAlarm) {
            hideSafetyAlarm();
        }
    }

    // Physical Calculations (20170516 Formula)
    const pMechKw = (currentTorque * currentSpeed) / 9549.3;
    const effFactor = Math.max(0.85, 0.94 - (currentSpeed / 3000.0) * 0.06);
    const pElecKw = pMechKw > 0.05 ? pMechKw / effFactor : 0.4;
    const vSigma = 299.1 + (Math.random() - 0.5) * 0.4;
    const pfSigma = 0.71 + (Math.random() - 0.5) * 0.02;
    const iSigma = pElecKw > 0.05 ? (pElecKw * 1000.0) / (Math.sqrt(3) * vSigma * pfSigma) : 0.0;
    const efficiency = pElecKw > 0.05 ? Math.min(100, (pMechKw / pElecKw) * 100) : 0;
    const kt = iSigma > 0.1 ? currentTorque / iSigma : 0;

    // Thermal simulation
    simState.temp += (pElecKw - pMechKw) * 0.006 + (Math.random() - 0.5) * 0.05;
    simState.temp = Math.max(20, Math.min(150, simState.temp));

    // Update UI Cards
    document.getElementById('valSpeed').textContent = currentSpeed.toFixed(1);
    document.getElementById('valTorque').textContent = currentTorque.toFixed(1);
    document.getElementById('valPower').textContent = pMechKw.toFixed(2);
    document.getElementById('valEfficiency').textContent = efficiency.toFixed(1);
    document.getElementById('valKt').textContent = kt.toFixed(2);
    document.getElementById('valTemp1').textContent = simState.temp.toFixed(1);

    // Push to Chart
    const nowStr = new Date().toTimeString().split(' ')[0];
    labels.push(nowStr);
    dataTorque.push(parseFloat(currentTorque.toFixed(1)));
    dataSpeed.push(parseFloat(currentSpeed.toFixed(1)));
    dataTemp.push(parseFloat(simState.temp.toFixed(1)));
    dataPower.push(parseFloat(pMechKw.toFixed(2)));

    if (labels.length > maxChartPoints) {
        labels.shift(); dataTorque.shift(); dataSpeed.shift(); dataTemp.shift(); dataPower.shift();
    }
    chartTN.update();
    chartTemp.update();

    return {
        time: nowStr,
        speed: currentSpeed,
        torque: currentTorque,
        pMech: pMechKw,
        pElec: pElecKw,
        eff: efficiency,
        kt: kt,
        temp: simState.temp
    };
}

// Manual Test Buttons
const btnStart = document.getElementById('btnStart');
const btnStop = document.getElementById('btnStop');
const btnLock = document.getElementById('btnLockTorque');
const btnExport = document.getElementById('btnExportCsv');

btnStart.addEventListener('click', () => {
    if (isRunning) return;
    isRunning = true;
    testStartTime = new Date();
    testRecords = [];
    btnStart.disabled = true;
    btnStop.disabled = false;
    manualTimerId = setInterval(() => {
        const targetRpm = parseFloat(sliderSpeed.value);
        const targetTorque = parseFloat(sliderTorque.value);
        const rec = calculateAndTick(targetRpm, targetTorque);
        testRecords.push(rec);
    }, 300);
});

btnStop.addEventListener('click', () => {
    if (!isRunning) return;
    isRunning = false;
    clearInterval(manualTimerId);
    btnStart.disabled = false;
    btnStop.disabled = true;
});

btnLock.addEventListener('click', () => {
    isLocked = !isLocked;
    btnLock.textContent = isLocked ? '🔓 解除鎖定 (Unlock)' : '🔒 轉矩鎖定 (Lock)';
    btnLock.classList.toggle('btn-primary', isLocked);
    btnLock.classList.toggle('btn-secondary', !isLocked);
});

// ===============================================================================
// 📈 TAB 2: MULTI-STEP T-N CURVE AUTO TEST
// ===============================================================================
const btnStartTnAuto = document.getElementById('btnStartTnAuto');
const btnStopTnAuto = document.getElementById('btnStopTnAuto');
const btnExportTnCsv = document.getElementById('btnExportTnCsv');
const tnProgressBar = document.getElementById('tnProgressBar');
const tnProgressText = document.getElementById('tnProgressText');
const tnStatusText = document.getElementById('tnStatusText');
const tnDwellCountdown = document.getElementById('tnDwellCountdown');
const tableTnBody = document.querySelector('#tableTnPoints tbody');

const ctxTnResult = document.getElementById('chartTnResult').getContext('2d');
let chartTnResult = new Chart(ctxTnResult, {
    type: 'line',
    data: {
        labels: [],
        datasets: [
            { label: '實測功率 (kW)', borderColor: '#00f076', backgroundColor: 'rgba(0, 240, 118, 0.1)', borderWidth: 2.5, data: [], yAxisID: 'yPower' },
            { label: '系統效率 (%)', borderColor: '#9d4edd', backgroundColor: 'rgba(157, 78, 221, 0.1)', borderWidth: 2, borderDash: [4, 4], data: [], yAxisID: 'yEff' }
        ]
    },
    options: {
        responsive: true, maintainAspectRatio: false,
        scales: {
            x: { title: { display: true, text: '轉速 (rpm)', color: '#94a3b8' }, grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#94a3b8' } },
            yPower: { type: 'linear', position: 'left', title: { display: true, text: '功率 (kW)', color: '#00f076' }, ticks: { color: '#00f076' } },
            yEff: { type: 'linear', position: 'right', min: 0, max: 100, title: { display: true, text: '效率 (%)', color: '#9d4edd' }, ticks: { color: '#9d4edd' }, grid: { drawOnChartArea: false } }
        }
    }
});

function initTnSequence() {
    const startRpm = parseFloat(document.getElementById('tnStartRpm').value) || 100;
    const stepRpm = parseFloat(document.getElementById('tnStepRpm').value) || 100;
    const endRpm = parseFloat(document.getElementById('tnEndRpm').value) || 1000;
    const fixedTorque = parseFloat(document.getElementById('tnFixedTorque').value) || 30.0;
    const dwellTime = parseFloat(document.getElementById('tnDwellTime').value) || 10.0;

    tnTestSequence = [];
    let rpm = startRpm;
    let idx = 1;
    while (rpm <= endRpm + 0.1) {
        tnTestSequence.push({ idx: idx++, targetRpm: rpm, targetTorque: fixedTorque, dwellSec: dwellTime });
        rpm += stepRpm;
    }

    tableTnBody.innerHTML = '';
    tnTestSequence.forEach(pt => {
        const tr = document.createElement('tr');
        tr.id = `tn-row-${pt.idx}`;
        tr.innerHTML = `
            <td>#${pt.idx}</td>
            <td>${pt.targetRpm} rpm</td>
            <td class="col-speed">--</td>
            <td class="col-trq">--</td>
            <td class="col-pwr">--</td>
            <td class="col-eff">--</td>
            <td class="col-kt">--</td>
            <td class="col-status"><span class="badge">等待中</span></td>
        `;
        tableTnBody.appendChild(tr);
    });

    tnRecordedPoints = [];
    chartTnResult.data.labels = [];
    chartTnResult.data.datasets[0].data = [];
    chartTnResult.data.datasets[1].data = [];
    chartTnResult.update();
}

btnStartTnAuto.addEventListener('click', () => {
    initTnSequence();
    tnCurrentStepIdx = 0;
    btnStartTnAuto.disabled = true;
    btnStopTnAuto.disabled = false;
    runNextTnStep();
});

btnStopTnAuto.addEventListener('click', () => {
    clearInterval(tnTimerId);
    clearInterval(tnDwellTimerId);
    tnStatusText.textContent = '已手動終止';
    btnStartTnAuto.disabled = false;
    btnStopTnAuto.disabled = true;
});

function runNextTnStep() {
    if (tnCurrentStepIdx >= tnTestSequence.length) {
        clearInterval(tnTimerId);
        clearInterval(tnDwellTimerId);
        tnStatusText.textContent = '🎉 全部 10 點測試完成！';
        tnProgressBar.style.width = '100%';
        tnProgressText.textContent = `${tnTestSequence.length} / ${tnTestSequence.length} 點 (100%)`;
        btnStartTnAuto.disabled = false;
        btnStopTnAuto.disabled = true;
        return;
    }

    const currentPt = tnTestSequence[tnCurrentStepIdx];
    const totalPts = tnTestSequence.length;
    const progressPct = ((tnCurrentStepIdx) / totalPts) * 100;
    tnProgressBar.style.width = `${progressPct}%`;
    tnProgressText.textContent = `${tnCurrentStepIdx + 1} / ${totalPts} 點 (${progressPct.toFixed(0)}%)`;
    tnStatusText.textContent = `加速並加載至 ${currentPt.targetRpm} rpm @ ${currentPt.targetTorque} Nm...`;

    const row = document.getElementById(`tn-row-${currentPt.idx}`);
    if (row) row.querySelector('.col-status').innerHTML = '<span class="badge" style="color:#00f2fe">穩定加載中</span>';

    clearInterval(tnTimerId);
    tnTimerId = setInterval(() => {
        calculateAndTick(currentPt.targetRpm, currentPt.targetTorque);
    }, 200);

    tnDwellRemaining = currentPt.dwellSec;
    tnDwellCountdown.textContent = `${tnDwellRemaining} s`;
    
    clearInterval(tnDwellTimerId);
    tnDwellTimerId = setInterval(() => {
        tnDwellRemaining--;
        tnDwellCountdown.textContent = `${tnDwellRemaining} s`;
        if (tnDwellRemaining <= 0) {
            clearInterval(tnDwellTimerId);
            const sample = calculateAndTick(currentPt.targetRpm, currentPt.targetTorque);
            tnRecordedPoints.push({
                idx: currentPt.idx,
                targetRpm: currentPt.targetRpm,
                speed: sample.speed,
                torque: sample.torque,
                pMech: sample.pMech,
                eff: sample.eff,
                kt: sample.kt
            });

            if (row) {
                row.querySelector('.col-speed').textContent = sample.speed.toFixed(1) + ' rpm';
                row.querySelector('.col-trq').textContent = sample.torque.toFixed(2) + ' Nm';
                row.querySelector('.col-pwr').textContent = sample.pMech.toFixed(2);
                row.querySelector('.col-eff').textContent = sample.eff.toFixed(1) + '%';
                row.querySelector('.col-kt').textContent = sample.kt.toFixed(2);
                row.querySelector('.col-status').innerHTML = '<span class="badge" style="color:#00f076">✓ 已採樣</span>';
            }

            chartTnResult.data.labels.push(sample.speed.toFixed(0));
            chartTnResult.data.datasets[0].data.push(sample.pMech.toFixed(2));
            chartTnResult.data.datasets[1].data.push(sample.eff.toFixed(1));
            chartTnResult.update();

            tnCurrentStepIdx++;
            runNextTnStep();
        }
    }, 1000);
}

btnExportTnCsv.addEventListener('click', () => {
    if (tnRecordedPoints.length === 0) {
        alert('尚未有已採樣之 T-N 數據。');
        return;
    }
    let csv = '\uFEFF多段 T-N 曲線測試報表 (固定 30Nm 加載)\r\n點位,目標轉速(rpm),實測轉速(rpm),實測轉矩(Nm),輸出功率(kW),效率(%),扭力常數Kt(Nm/A)\r\n';
    tnRecordedPoints.forEach(p => {
        csv += `${p.idx},${p.targetRpm},${p.speed.toFixed(1)},${p.torque.toFixed(2)},${p.pMech.toFixed(2)},${p.eff.toFixed(1)},${p.kt.toFixed(2)}\r\n`;
    });
    downloadCsvFile(csv, `Report_TN_MultiStep_${new Date().getTime()}.csv`);
});

// ===============================================================================
// 🗺️ TAB 3: 2D EFFICIENCY MAP AUTOMATED SCAN & HEATMAP CANVAS
// ===============================================================================
const btnStartEffScan = document.getElementById('btnStartEffScan');
const btnStopEffScan = document.getElementById('btnStopEffScan');
const btnExportEffCsv = document.getElementById('btnExportEffCsv');
const effProgressBar = document.getElementById('effProgressBar');
const effProgressText = document.getElementById('effProgressText');
const effCurrentPointText = document.getElementById('effCurrentPointText');
const effDwellCountdown = document.getElementById('effDwellCountdown');
const totalGridPointsDisp = document.getElementById('totalGridPointsDisp');
const canvasEff = document.getElementById('canvasEfficiencyMap');
const ctxEff = canvasEff.getContext('2d');

function updateEffGridCalc() {
    const startRpm = parseFloat(document.getElementById('effStartRpm').value) || 100;
    const endRpm = parseFloat(document.getElementById('effEndRpm').value) || 1000;
    const stepRpm = parseFloat(document.getElementById('effStepRpm').value) || 100;
    const startTrq = parseFloat(document.getElementById('effStartTrq').value) || 0;
    const endTrq = parseFloat(document.getElementById('effEndTrq').value) || 30;
    const stepTrq = parseFloat(document.getElementById('effStepTrq').value) || 2;

    const rpmCount = Math.floor((endRpm - startRpm) / stepRpm) + 1;
    const trqCount = Math.floor((endTrq - startTrq) / stepTrq) + 1;
    const total = rpmCount * trqCount;
    totalGridPointsDisp.textContent = `${rpmCount} × ${trqCount} = ${total} 網格點`;
}

document.querySelectorAll('#tab-eff-map input').forEach(input => {
    input.addEventListener('input', updateEffGridCalc);
});
updateEffGridCalc();

function initEffScanSequence() {
    const startRpm = parseFloat(document.getElementById('effStartRpm').value) || 100;
    const endRpm = parseFloat(document.getElementById('effEndRpm').value) || 1000;
    const stepRpm = parseFloat(document.getElementById('effStepRpm').value) || 100;
    const startTrq = parseFloat(document.getElementById('effStartTrq').value) || 0;
    const endTrq = parseFloat(document.getElementById('effEndTrq').value) || 30;
    const stepTrq = parseFloat(document.getElementById('effStepTrq').value) || 2;
    const dwellTime = parseFloat(document.getElementById('effDwellTime').value) || 2.0;

    effGridSequence = [];
    effMatrixData = {};

    let rpm = startRpm;
    let idx = 1;
    while (rpm <= endRpm + 0.1) {
        let trq = startTrq;
        while (trq <= endTrq + 0.01) {
            effGridSequence.push({ idx: idx++, rpm: rpm, trq: trq, dwellSec: dwellTime });
            trq += stepTrq;
        }
        rpm += stepRpm;
    }
}

btnStartEffScan.addEventListener('click', () => {
    initEffScanSequence();
    effCurrentStepIdx = 0;
    btnStartEffScan.disabled = true;
    btnStopEffScan.disabled = false;
    runNextEffStep();
});

btnStopEffScan.addEventListener('click', () => {
    clearInterval(effTimerId);
    clearInterval(effDwellTimerId);
    effCurrentPointText.textContent = '已手動終止';
    btnStartEffScan.disabled = false;
    btnStopEffScan.disabled = true;
});

function runNextEffStep() {
    if (effCurrentStepIdx >= effGridSequence.length) {
        clearInterval(effTimerId);
        clearInterval(effDwellTimerId);
        effCurrentPointText.textContent = '🎉 效率地圖掃描完成！';
        effProgressBar.style.width = '100%';
        effProgressText.textContent = `${effGridSequence.length} / ${effGridSequence.length} 點 (100%)`;
        btnStartEffScan.disabled = false;
        btnStopEffScan.disabled = true;
        drawEfficiencyMapHeatmap();
        return;
    }

    const pt = effGridSequence[effCurrentStepIdx];
    const totalPts = effGridSequence.length;
    const progressPct = (effCurrentStepIdx / totalPts) * 100;
    effProgressBar.style.width = `${progressPct}%`;
    effProgressText.textContent = `${effCurrentStepIdx + 1} / ${totalPts} 點 (${progressPct.toFixed(0)}%)`;
    effCurrentPointText.textContent = `[#${pt.idx}] 轉速: ${pt.rpm} rpm | 轉矩: ${pt.trq} Nm`;

    clearInterval(effTimerId);
    effTimerId = setInterval(() => {
        calculateAndTick(pt.rpm, pt.trq);
    }, 150);

    effDwellRemaining = pt.dwellSec;
    effDwellCountdown.textContent = `${effDwellRemaining} s`;

    clearInterval(effDwellTimerId);
    effDwellTimerId = setInterval(() => {
        effDwellRemaining--;
        effDwellCountdown.textContent = `${effDwellRemaining} s`;
        if (effDwellRemaining <= 0) {
            clearInterval(effDwellTimerId);
            const sample = calculateAndTick(pt.rpm, pt.trq);
            
            const key = `${pt.rpm}_${pt.trq}`;
            effMatrixData[key] = {
                rpm: pt.rpm,
                trq: pt.trq,
                eff: sample.eff,
                pMech: sample.pMech
            };

            drawEfficiencyMapHeatmap();
            effCurrentStepIdx++;
            runNextEffStep();
        }
    }, 1000);
}

function drawEfficiencyMapHeatmap() {
    const width = canvasEff.width;
    const height = canvasEff.height;
    ctxEff.fillStyle = '#080c14';
    ctxEff.fillRect(0, 0, width, height);

    const margin = { left: 70, right: 50, top: 40, bottom: 50 };
    const plotW = width - margin.left - margin.right;
    const plotH = height - margin.top - margin.bottom;

    const startRpm = 100, endRpm = 1000;
    const startTrq = 0, endTrq = 30;

    ctxEff.strokeStyle = 'rgba(255,255,255,0.08)';
    ctxEff.lineWidth = 1;
    ctxEff.strokeRect(margin.left, margin.top, plotW, plotH);

    ctxEff.fillStyle = '#94a3b8';
    ctxEff.font = '11px sans-serif';
    ctxEff.textAlign = 'center';
    for (let r = startRpm; r <= endRpm; r += 100) {
        const x = margin.left + ((r - startRpm) / (endRpm - startRpm)) * plotW;
        ctxEff.fillText(`${r}`, x, height - margin.bottom + 20);
        ctxEff.beginPath(); ctxEff.moveTo(x, margin.top); ctxEff.lineTo(x, margin.top + plotH); ctxEff.stroke();
    }
    ctxEff.fillText('轉速 Speed (rpm)', margin.left + plotW / 2, height - margin.bottom + 40);

    ctxEff.textAlign = 'right';
    for (let t = startTrq; t <= endTrq; t += 5) {
        const y = margin.top + plotH - ((t - startTrq) / (endTrq - startTrq)) * plotH;
        ctxEff.fillText(`${t} Nm`, margin.left - 10, y + 4);
        ctxEff.beginPath(); ctxEff.moveTo(margin.left, y); ctxEff.lineTo(margin.left + plotW, y); ctxEff.stroke();
    }

    ctxEff.save();
    ctxEff.translate(20, margin.top + plotH / 2);
    ctxEff.rotate(-Math.PI / 2);
    ctxEff.textAlign = 'center';
    ctxEff.fillText('加載轉矩 Torque (Nm)', 0, 0);
    ctxEff.restore();

    const cellW = plotW / 10;
    const cellH = plotH / 16;

    for (const key in effMatrixData) {
        const pt = effMatrixData[key];
        const x = margin.left + ((pt.rpm - startRpm) / (endRpm - startRpm)) * (plotW - cellW);
        const y = margin.top + plotH - ((pt.trq - startTrq) / (endTrq - startTrq)) * (plotH - cellH) - cellH;

        const eff = pt.eff;
        let color = '#3b82f6';
        if (eff >= 92) color = '#00f076';
        else if (eff >= 88) color = '#10b981';
        else if (eff >= 80) color = '#06b6d4';
        else if (eff >= 70) color = '#6366f1';
        else color = '#ef4444';

        ctxEff.fillStyle = color;
        ctxEff.globalAlpha = 0.85;
        ctxEff.fillRect(x, y, cellW, cellH);

        ctxEff.globalAlpha = 1.0;
        ctxEff.fillStyle = '#000';
        ctxEff.font = '9px monospace';
        ctxEff.textAlign = 'center';
        if (cellW > 35) {
            ctxEff.fillText(`${eff.toFixed(0)}%`, x + cellW / 2, y + cellH / 2 + 3);
        }
    }
}

setTimeout(drawEfficiencyMapHeatmap, 500);

btnExportEffCsv.addEventListener('click', () => {
    const keys = Object.keys(effMatrixData);
    if (keys.length === 0) {
        alert('尚未有已掃描之 2D 效率地圖數據。');
        return;
    }
    let csv = '\uFEFF馬達 2D 效率地圖數據矩陣 (Efficiency Map Matrix)\r\n轉速(rpm),轉矩(Nm),輸出功率(kW),系統效率(%)\r\n';
    keys.forEach(k => {
        const pt = effMatrixData[k];
        csv += `${pt.rpm},${pt.trq},${pt.pMech.toFixed(2)},${pt.eff.toFixed(2)}\r\n`;
    });
    downloadCsvFile(csv, `Report_Efficiency_Map_${new Date().getTime()}.csv`);
});

// ===============================================================================
// ⏱️ TAB 4: S1 / S2 / S6 DUTY CYCLE TESTING (IEC 60034-1)
// ===============================================================================
const dutyButtons = document.querySelectorAll('.duty-type-btn');
dutyButtons.forEach(btn => {
    btn.addEventListener('click', () => {
        dutyButtons.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        currentDutyMode = btn.getAttribute('data-duty');

        document.querySelectorAll('.duty-panel').forEach(p => p.classList.remove('active'));
        document.getElementById(`duty-panel-${currentDutyMode}`).classList.add('active');
    });
});

const btnStartDutyTest = document.getElementById('btnStartDutyTest');
const btnStopDutyTest = document.getElementById('btnStopDutyTest');
const btnExportDutyCsv = document.getElementById('btnExportDutyCsv');
const dutyProgressBar = document.getElementById('dutyProgressBar');
const dutyProgressText = document.getElementById('dutyProgressText');
const dutyStatusText = document.getElementById('dutyStatusText');
const thermalStatusText = document.getElementById('thermalStatusText');
const s6PhaseBadge = document.getElementById('s6PhaseBadge');

// Duty Charts
const ctxDutyWave = document.getElementById('chartDutyWaveform').getContext('2d');
const chartDutyWaveform = new Chart(ctxDutyWave, {
    type: 'line',
    data: {
        labels: [],
        datasets: [
            { label: '轉矩 (Nm)', borderColor: '#ff9900', borderWidth: 2, pointRadius: 0, data: [], yAxisID: 'yTrq' },
            { label: '轉速 (rpm)', borderColor: '#00f2fe', borderWidth: 1.5, pointRadius: 0, data: [], yAxisID: 'ySpd' }
        ]
    },
    options: {
        responsive: true, maintainAspectRatio: false, animation: false,
        scales: {
            x: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#64748b', font: { size: 10 } } },
            yTrq: { type: 'linear', position: 'left', ticks: { color: '#ff9900' }, title: { display: true, text: 'Torque (Nm)', color: '#ff9900' } },
            ySpd: { type: 'linear', position: 'right', ticks: { color: '#00f2fe' }, grid: { drawOnChartArea: false }, title: { display: true, text: 'Speed (rpm)', color: '#00f2fe' } }
        }
    }
});

const ctxDutyTemp = document.getElementById('chartDutyTemp').getContext('2d');
const chartDutyTemp = new Chart(ctxDutyTemp, {
    type: 'line',
    data: {
        labels: [],
        datasets: [
            { label: 'KTY84 溫升 (°C)', borderColor: '#ff3366', backgroundColor: 'rgba(255,51,102,0.1)', borderWidth: 2, pointRadius: 0, data: [] }
        ]
    },
    options: {
        responsive: true, maintainAspectRatio: false, animation: false,
        scales: {
            x: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#64748b', font: { size: 10 } } },
            y: { title: { display: true, text: '溫度 (°C)', color: '#ff3366' }, ticks: { color: '#ff3366' }, suggestedMin: 20, suggestedMax: 130 }
        }
    }
});

btnStartDutyTest.addEventListener('click', () => {
    if (dutyIsRunning) return;
    dutyIsRunning = true;
    dutyElapsedSec = 0;
    dutyRecords = [];
    tempHistoryForEquil = [];
    
    chartDutyWaveform.data.labels = [];
    chartDutyWaveform.data.datasets[0].data = [];
    chartDutyWaveform.data.datasets[1].data = [];
    chartDutyTemp.data.labels = [];
    chartDutyTemp.data.datasets[0].data = [];

    btnStartDutyTest.disabled = true;
    btnStopDutyTest.disabled = false;

    if (currentDutyMode === 'S1') {
        const speed = parseFloat(document.getElementById('s1Speed').value) || 1500;
        const trq = parseFloat(document.getElementById('s1Torque').value) || 30;
        const durMin = parseFloat(document.getElementById('s1DurationMin').value) || 30;
        dutyTotalSec = durMin * 60;
        dutyStatusText.textContent = `S1 額定連續運轉 (目標 ${speed} rpm @ ${trq} Nm)`;
        runS1Loop(speed, trq);
    } else if (currentDutyMode === 'S2') {
        const speed = parseFloat(document.getElementById('s2Speed').value) || 1500;
        const trq = parseFloat(document.getElementById('s2Torque').value) || 45;
        const durMin = parseFloat(document.getElementById('s2DurationMin').value) || 10;
        dutyTotalSec = durMin * 60;
        dutyStatusText.textContent = `S2 短時超載運轉 (目標 ${speed} rpm @ ${trq} Nm, ${durMin} 分鐘)`;
        runS2Loop(speed, trq);
    } else if (currentDutyMode === 'S6') {
        const speed = parseFloat(document.getElementById('s6Speed').value) || 1500;
        const trq = parseFloat(document.getElementById('s6Torque').value) || 35;
        const edPct = parseFloat(document.getElementById('s6EdPct').value) || 40;
        const cycleSec = parseFloat(document.getElementById('s6CycleSec').value) || 60;
        dutyTotalCycles = parseInt(document.getElementById('s6Cycles').value) || 5;
        dutyTotalSec = cycleSec * dutyTotalCycles;
        dutyCurrentCycle = 1;
        dutyPhase = 'load';
        dutyPhaseElapsed = 0;
        dutyStatusText.textContent = `S6 週期性加載 (S6-${edPct}%, 循環 ${dutyCurrentCycle}/${dutyTotalCycles})`;
        runS6Loop(speed, trq, cycleSec, edPct);
    }
});

btnStopDutyTest.addEventListener('click', () => {
    stopDutyTest('已手動終止測試');
});

function stopDutyTest(reason = '測試結束') {
    dutyIsRunning = false;
    clearInterval(dutyTimerId);
    clearInterval(dutySecondTimerId);
    dutyStatusText.textContent = `⏹ ${reason}`;
    btnStartDutyTest.disabled = false;
    btnStopDutyTest.disabled = true;
}

// S1 Continuous Loop
function runS1Loop(speed, trq) {
    const autoEquil = document.getElementById('s1AutoEquil').checked;
    const maxTemp = parseFloat(document.getElementById('s1MaxTemp').value) || 130;

    dutyTimerId = setInterval(() => {
        const sample = calculateAndTick(speed, trq);
        recordDutyPoint(sample);
    }, 200);

    dutySecondTimerId = setInterval(() => {
        dutyElapsedSec++;
        updateDutyProgress();

        // Check Max Temp Safety
        if (simState.temp >= maxTemp) {
            stopDutyTest(`🚨 超溫保護觸發 (溫度 ${simState.temp.toFixed(1)}°C >= ${maxTemp}°C)！已自動安全停機。`);
            return;
        }

        // Thermal Equilibrium Check: Delta T < 1 degC in last 60 seconds (simulated window)
        tempHistoryForEquil.push({ sec: dutyElapsedSec, temp: simState.temp });
        if (tempHistoryForEquil.length > 30) {
            const pastTemp = tempHistoryForEquil[tempHistoryForEquil.length - 30].temp;
            const deltaT = Math.abs(simState.temp - pastTemp);
            if (deltaT < 0.2) {
                thermalStatusText.textContent = '✨ 已達到熱平衡狀態';
                thermalStatusText.style.color = '#00f076';
                if (autoEquil && dutyElapsedSec > 15) {
                    stopDutyTest('✨ 馬達已達熱平衡狀態，S1 連續運轉測試自動完成！');
                    return;
                }
            } else {
                thermalStatusText.textContent = `溫升中 (ΔT: ${deltaT.toFixed(2)}°C)`;
                thermalStatusText.style.color = '#ffcc00';
            }
        }

        if (dutyElapsedSec >= dutyTotalSec) {
            stopDutyTest('S1 時間到達，測試完成！');
        }
    }, 1000);
}

// S2 Short-Time Loop
function runS2Loop(speed, trq) {
    const maxTemp = parseFloat(document.getElementById('s2MaxTemp').value) || 140;

    dutyTimerId = setInterval(() => {
        const sample = calculateAndTick(speed, trq);
        recordDutyPoint(sample);
    }, 200);

    dutySecondTimerId = setInterval(() => {
        dutyElapsedSec++;
        updateDutyProgress();

        if (simState.temp >= maxTemp) {
            stopDutyTest(`🚨 超溫保護觸發 (溫度 ${simState.temp.toFixed(1)}°C >= ${maxTemp}°C)！已自動安全停機。`);
            return;
        }

        if (dutyElapsedSec >= dutyTotalSec) {
            stopDutyTest('S2 短時超載時間到達，請開始停機冷卻！');
        }
    }, 1000);
}

// S6 Periodic Loop
function runS6Loop(speed, trq, cycleSec, edPct) {
    const tLoad = cycleSec * (edPct / 100.0);
    const tIdle = cycleSec - tLoad;

    dutyTimerId = setInterval(() => {
        const currentTrq = dutyPhase === 'load' ? trq : 0.0;
        const sample = calculateAndTick(speed, currentTrq);
        recordDutyPoint(sample);
    }, 200);

    dutySecondTimerId = setInterval(() => {
        dutyElapsedSec++;
        dutyPhaseElapsed++;
        updateDutyProgress();

        if (dutyPhase === 'load') {
            s6PhaseBadge.textContent = `🔴 加載階段 (${(tLoad - dutyPhaseElapsed)}s)`;
            s6PhaseBadge.style.color = '#ff3366';
            if (dutyPhaseElapsed >= tLoad) {
                dutyPhase = 'idle';
                dutyPhaseElapsed = 0;
            }
        } else {
            s6PhaseBadge.textContent = `🟢 空載階段 (${(tIdle - dutyPhaseElapsed)}s)`;
            s6PhaseBadge.style.color = '#00f076';
            if (dutyPhaseElapsed >= tIdle) {
                dutyPhase = 'load';
                dutyPhaseElapsed = 0;
                dutyCurrentCycle++;
                if (dutyCurrentCycle > dutyTotalCycles) {
                    stopDutyTest(`S6 全部 ${dutyTotalCycles} 次週期循環測試完成！`);
                    return;
                }
                dutyStatusText.textContent = `S6 週期性加載 (循環 ${dutyCurrentCycle}/${dutyTotalCycles})`;
            }
        }

        if (dutyElapsedSec >= dutyTotalSec) {
            stopDutyTest('S6 測試循環結束！');
        }
    }, 1000);
}

function updateDutyProgress() {
    const pct = Math.min(100, (dutyElapsedSec / dutyTotalSec) * 100);
    dutyProgressBar.style.width = `${pct}%`;
    dutyProgressText.textContent = `${pct.toFixed(0)}% (${dutyElapsedSec} / ${dutyTotalSec} 秒)`;
}

function recordDutyPoint(sample) {
    dutyRecords.push({
        sec: dutyElapsedSec,
        mode: currentDutyMode,
        speed: sample.speed,
        torque: sample.torque,
        pMech: sample.pMech,
        pElec: sample.pElec,
        eff: sample.eff,
        temp: sample.temp
    });

    if (dutyRecords.length % 3 === 0) {
        chartDutyWaveform.data.labels.push(`${dutyElapsedSec}s`);
        chartDutyWaveform.data.datasets[0].data.push(sample.torque.toFixed(2));
        chartDutyWaveform.data.datasets[1].data.push(sample.speed.toFixed(0));

        chartDutyTemp.data.labels.push(`${dutyElapsedSec}s`);
        chartDutyTemp.data.datasets[0].data.push(sample.temp.toFixed(1));

        if (chartDutyWaveform.data.labels.length > 50) {
            chartDutyWaveform.data.labels.shift();
            chartDutyWaveform.data.datasets[0].data.shift();
            chartDutyWaveform.data.datasets[1].data.shift();
            chartDutyTemp.data.labels.shift();
            chartDutyTemp.data.datasets[0].data.shift();
        }

        chartDutyWaveform.update();
        chartDutyTemp.update();
    }
}

// Export Duty Cycle CSV
btnExportDutyCsv.addEventListener('click', () => {
    if (dutyRecords.length === 0) {
        alert('尚未有已記錄之工作制數據。');
        return;
    }
    let csv = `\uFEFFIEC 60034-1 馬達 ${currentDutyMode} 工作制測試報表\r\n時間(秒),工作制,實測轉速(rpm),實測轉矩(Nm),輸出功率(kW),輸入電功率(kW),效率(%),KTY84溫度(degC)\r\n`;
    dutyRecords.forEach(r => {
        csv += `${r.sec},${r.mode},${r.speed.toFixed(1)},${r.torque.toFixed(2)},${r.pMech.toFixed(2)},${r.pElec.toFixed(2)},${r.eff.toFixed(1)},${r.temp.toFixed(1)}\r\n`;
    });
    downloadCsvFile(csv, `Report_DutyCycle_${currentDutyMode}_${new Date().getTime()}.csv`);
});

// Helper: Download CSV
function downloadCsvFile(content, filename) {
    const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
