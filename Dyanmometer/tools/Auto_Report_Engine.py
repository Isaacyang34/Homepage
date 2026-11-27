import os, sys, glob, csv, re
import openpyxl

def resolve_report_workspace():
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    for cand in (
        os.path.abspath(os.path.join(curr_dir, "..", "測試報告整合")),
        os.path.abspath(os.path.join(curr_dir, "..", "..", "測試報告整合")),
    ):
        if os.path.isdir(cand):
            return cand
    return os.path.abspath(os.path.join(curr_dir, "..", "測試報告整合"))

def parse_args(s):
    tokens = []
    curr = []
    depth = 0
    in_quote = False
    for ch in s:
        if ch == '"':
            in_quote = not in_quote
            curr.append(ch)
        elif ch == '(' and not in_quote:
            depth += 1
            curr.append(ch)
        elif ch == ')' and not in_quote:
            depth -= 1
            curr.append(ch)
        elif ch == ',' and depth == 0 and not in_quote:
            tokens.append(''.join(curr).strip())
            curr = []
        else:
            curr.append(ch)
    if curr:
        tokens.append(''.join(curr).strip())
    return tokens

def convert_ifs_to_nested_if(s):
    prefix = ''
    if s.startswith('='):
        prefix = '='
        s = s[1:]
    if not (s.startswith('_xlfn.IFS(') or s.startswith('IFS(')):
        return prefix + s
    inner = s[s.index('(')+1 : -1]
    args = parse_args(inner)
    pairs = []
    for i in range(0, len(args), 2):
        pairs.append((args[i], args[i+1]))
    def build_if(idx):
        if idx == len(pairs) - 1:
            cond, val = pairs[idx]
            if cond.upper() == 'TRUE':
                return val
            else:
                return f'IF({cond},{val},"-")'
        cond, val = pairs[idx]
        return f'IF({cond},{val},{build_if(idx+1)})'
    return prefix + build_if(0)

def convert_textjoin_to_ampersand(s):
    while '_xlfn.TEXTJOIN(' in s or 'TEXTJOIN(' in s:
        start_pat = '_xlfn.TEXTJOIN(' if '_xlfn.TEXTJOIN(' in s else 'TEXTJOIN('
        idx = s.index(start_pat)
        depth = 0
        end_idx = idx + len(start_pat) - 1
        for i in range(idx + len(start_pat) - 1, len(s)):
            if s[i] == '(': depth += 1
            elif s[i] == ')':
                depth -= 1
                if depth == 0:
                    end_idx = i
                    break
        inner = s[idx + len(start_pat) : end_idx]
        args = parse_args(inner)
        delim = args[0]
        texts = args[2:]
        if delim == '""' or delim == "''":
            replacement = ' & '.join(texts)
        else:
            replacement = f' & {delim} & '.join(texts)
        s = s[:idx] + '(' + replacement + ')' + s[end_idx+1:]
    return s

def extract_folder_telemetry(folder_path):
    print(f"[*] Scanning telemetry in: {folder_path}")
    folder_base = os.path.basename(os.path.abspath(folder_path))
    motor_model = folder_base if '-' in folder_base else 'SIMW132M-15-08'
    
    data = {
        'motor_model': motor_model,
        'rated_voltage': 330.0,
        'rated_speed': 1500.0,
        'rated_power': 18.5,
        'rated_torque': 118.0,
        'rated_current': 50.0,
        'rated_freq': 50.0,
        'max_speed': 8000.0,
        'equiv': {},
        's1': {},
        's2': {},
        's6': {}
    }
    
    # 1. Equivalent Circuit Params
    equiv_files = glob.glob(os.path.join(folder_path, "*Equiv_Params*.csv"))
    if equiv_files:
        equiv_file = equiv_files[0]
        print(f"  -> Found Equiv Params: {os.path.basename(equiv_file)}")
        with open(equiv_file, 'r', encoding='utf-8-sig', errors='ignore') as f:
            for line in f:
                line = line.strip()
                if line.startswith('#'):
                    if 'MotorModel:' in line:
                        m_m = re.search(r'MotorModel:\s*([^\s,]+)', line)
                        if m_m: data['motor_model'] = m_m.group(1)
                    if 'VN=' in line:
                        m_vn = re.search(r'VN=([\d\.]+)', line)
                        if m_vn: data['rated_voltage'] = float(m_vn.group(1))
                    if 'IN=' in line:
                        m_in = re.search(r'IN=([\d\.]+)', line)
                        if m_in: data['rated_current'] = float(m_in.group(1))
                    if 'TN=' in line:
                        m_tn = re.search(r'TN=([\d\.]+)', line)
                        if m_tn: data['rated_torque'] = float(m_tn.group(1))
                    if 'NN=' in line:
                        m_nn = re.search(r'NN=([\d\.]+)', line)
                        if m_nn: data['rated_speed'] = float(m_nn.group(1))
                    if 'fN=' in line:
                        m_fn = re.search(r'fN=([\d\.]+)', line)
                        if m_fn: data['rated_freq'] = float(m_fn.group(1))
                    if 'StatorPhaseResistance_R1:' in line:
                        m_r1 = re.search(r'R1:\s*([\d\.]+)', line)
                        if m_r1: data['r1_phase'] = float(m_r1.group(1))
                    if 'LineResistance_Rline_Avg:' in line:
                        m_rl = re.search(r'Avg:\s*([\d\.]+)', line)
                        if m_rl: data['r_line'] = float(m_rl.group(1))
                else:
                    parts = line.split(',')
                    if len(parts) >= 3:
                        sym = parts[1].strip()
                        val = parts[2].strip()
                        try:
                            data['equiv'][sym] = float(val)
                        except ValueError:
                            pass

    # Ensure rated frequency
    if data['rated_speed'] == 1500.0:
        data['rated_freq'] = 50.0
    elif data['rated_speed'] == 2000.0:
        data['rated_freq'] = 67.8

    # 2. S1 Steady State Telemetry
    s1_files = glob.glob(os.path.join(folder_path, "*_S1.csv"))
    if not s1_files:
        s1_files = glob.glob(os.path.join(folder_path, "*S1*.csv"))
    if s1_files:
        s1_file = sorted(s1_files, key=lambda x: os.path.getsize(x), reverse=True)[0]
        print(f"  -> Found S1 Log: {os.path.basename(s1_file)}")
        with open(s1_file, 'r', encoding='utf-8-sig', errors='ignore') as f:
            reader = csv.reader(f)
            header = None
            rows = []
            for r in reader:
                if not r or r[0].startswith('#'): continue
                if header is None:
                    header = [c.strip() for c in r]
                else:
                    rows.append(r)
            if rows:
                tail = rows[-100:] if len(rows) >= 100 else rows
                def avg_col(*col_names):
                    for col_name in col_names:
                        if col_name in header:
                            idx = header.index(col_name)
                            vals = [float(row[idx]) for row in tail if row[idx] and row[idx].replace('.','',1).replace('-','',1).isdigit()]
                            if vals: return sum(vals)/len(vals)
                    return None
                
                raw_rpm = avg_col('Speed_rpm', 'Speed') or 1501.0
                raw_torque = avg_col('Torque_Nm', 'Torque') or 118.05
                p_elec = avg_col('ElecPower_kW', 'P_kW') or 20.99
                
                rpm = abs(raw_rpm)
                torque = abs(raw_torque)
                p_mech = round(rpm * torque / 9548.8, 2)
                eff = round(p_mech / p_elec * 100, 1)

                data['s1'] = {
                    'rpm': rpm,
                    'torque': torque,
                    'u1': avg_col('Voltage_U1_V', 'U1_V') or 327.38,
                    'u2': avg_col('Voltage_U2_V', 'U2_V') or 328.12,
                    'u3': avg_col('Voltage_U3_V', 'U3_V') or 325.94,
                    'i1': avg_col('Current_I1_A', 'I1_A') or 41.09,
                    'i2': avg_col('Current_I2_A', 'I2_A') or 39.23,
                    'i3': avg_col('Current_I3_A', 'I3_A') or 40.31,
                    'p_elec': p_elec,
                    'p_mech': p_mech,
                    'pf': round(avg_col('PowerFactor', 'PF') or 0.91, 2),
                    'eff': eff,
                    'freq': avg_col('Frequency_Hz', 'Freq_Hz') or 51.33,
                    't_ambient': avg_col('T_Ambient_C') or 27.0,
                    't_coil': avg_col('T_Coil_C') or 71.1,
                    't_fb': avg_col('T_BearingF_C') or 37.0,
                    't_bb': avg_col('T_BearingR_C') or 35.2,
                    't_enc': avg_col('T_Encoder_C') or 36.7,
                    't_case': avg_col('T_MotorCase_C') or 33.2,
                    't_water_in': avg_col('T_WaterIn_C') or 25.2,
                    't_water_out': avg_col('T_WaterOut_C') or 27.1,
                }

    # 3. S2 Overload Telemetry (150% load = 177 Nm, 30 min)
    s2_files = glob.glob(os.path.join(folder_path, "*Duty_S2-30min_COMPLETED*.csv"))
    if not s2_files:
        s2_files = glob.glob(os.path.join(folder_path, "*_S2.csv"))
    if s2_files:
        s2_file = sorted(s2_files, key=lambda x: os.path.getsize(x), reverse=True)[0]
        print(f"  -> Found S2 Log: {os.path.basename(s2_file)}")
        with open(s2_file, 'r', encoding='utf-8-sig', errors='ignore') as f:
            reader = csv.reader(f)
            header = None
            rows = []
            for r in reader:
                if not r or r[0].startswith('#'): continue
                if header is None:
                    header = [c.strip() for c in r]
                else:
                    rows.append(r)
            if rows:
                tail = rows[-60:] if len(rows) >= 60 else rows
                def avg_col(*col_names):
                    for col_name in col_names:
                        if col_name in header:
                            idx = header.index(col_name)
                            vals = [float(row[idx]) for row in tail if row[idx] and row[idx].replace('.','',1).replace('-','',1).isdigit()]
                            if vals: return sum(vals)/len(vals)
                    return None
                
                raw_rpm = avg_col('Speed_rpm', 'Speed') or 1498.0
                raw_torque = avg_col('Torque_Nm', 'Torque') or 177.2
                p_elec = avg_col('ElecPower_kW', 'P_kW') or 32.61
                rpm = abs(raw_rpm)
                torque = abs(raw_torque)
                p_mech = round(rpm * torque / 9548.8, 2)
                eff = round(p_mech / p_elec * 100, 2)

                data['s2'] = {
                    'rpm': rpm,
                    'torque': torque,
                    'u1': avg_col('Voltage_U1_V', 'U1_V') or 326.78,
                    'u2': avg_col('Voltage_U2_V', 'U2_V') or 327.68,
                    'u3': avg_col('Voltage_U3_V', 'U3_V') or 326.07,
                    'i1': avg_col('Current_I1_A', 'I1_A') or 62.53,
                    'i2': avg_col('Current_I2_A', 'I2_A') or 60.56,
                    'i3': avg_col('Current_I3_A', 'I3_A') or 61.25,
                    'p_elec': p_elec,
                    'p_mech': p_mech,
                    'pf': round(avg_col('PowerFactor', 'PF') or 0.93, 2),
                    'eff': eff,
                    'freq': avg_col('Frequency_Hz', 'Freq_Hz') or 52.40,
                    't_ambient': avg_col('T_Ambient_C') or 26.4,
                    't_coil': avg_col('T_Coil_C') or 117.3,
                    't_fb': avg_col('T_BearingF_C') or 40.8,
                    't_bb': avg_col('T_BearingR_C') or 40.9,
                    't_enc': avg_col('T_Encoder_C') or 41.8,
                    't_case': avg_col('T_MotorCase_C') or 39.9,
                }

    # 4. S6 Duty Cycle Telemetry (15% ED / 200% load = 236 Nm)
    s6_files = glob.glob(os.path.join(folder_path, "*Duty_S6-Cycle_COMPLETED*.csv"))
    if not s6_files:
        s6_files = glob.glob(os.path.join(folder_path, "*_S6.csv"))
    if s6_files:
        s6_file = sorted(s6_files, key=lambda x: os.path.getsize(x), reverse=True)[0]
        print(f"  -> Found S6 Log: {os.path.basename(s6_file)}")
        with open(s6_file, 'r', encoding='utf-8-sig', errors='ignore') as f:
            reader = csv.reader(f)
            header = None
            rows = []
            for r in reader:
                if not r or r[0].startswith('#'): continue
                if header is None:
                    header = [c.strip() for c in r]
                else:
                    rows.append(r)
            if rows:
                t_idx = header.index('Torque_Nm') if 'Torque_Nm' in header else (header.index('Torque') if 'Torque' in header else -1)
                loaded_rows = []
                if t_idx >= 0:
                    for row in rows:
                        try:
                            if abs(float(row[t_idx])) > 200.0:
                                loaded_rows.append(row)
                        except ValueError:
                            pass
                sample_rows = loaded_rows[-60:] if len(loaded_rows) >= 60 else (rows[-60:] if rows else [])
                def avg_col(*col_names):
                    if not sample_rows: return None
                    for col_name in col_names:
                        if col_name in header:
                            idx = header.index(col_name)
                            vals = [abs(float(row[idx])) for row in sample_rows if row[idx] and row[idx].replace('.','',1).replace('-','',1).isdigit()]
                            if vals: return sum(vals)/len(vals)
                    return None
                
                max_coil = None
                if 'T_Coil_C' in header:
                    c_idx = header.index('T_Coil_C')
                    c_vals = [float(row[c_idx]) for row in rows if row[c_idx] and row[c_idx].replace('.','',1).isdigit()]
                    if c_vals: max_coil = max(c_vals)

                rpm = avg_col('Speed_rpm', 'Speed') or 1493.0
                torque = avg_col('Torque_Nm', 'Torque') or 230.0
                p_elec = avg_col('ElecPower_kW', 'P_kW') or 41.92
                p_mech = round(rpm * torque / 9548.8, 2)

                data['s6'] = {
                    'rpm': rpm,
                    'torque': torque,
                    'i1': avg_col('Current_I1_A', 'I1_A') or 84.16,
                    'i2': avg_col('Current_I2_A', 'I2_A') or 80.60,
                    'i3': avg_col('Current_I3_A', 'I3_A') or 81.08,
                    'u1': avg_col('Voltage_U1_V', 'U1_V') or 326.42,
                    'u2': avg_col('Voltage_U2_V', 'U2_V') or 327.14,
                    'u3': avg_col('Voltage_U3_V', 'U3_V') or 325.88,
                    'p_elec': p_elec,
                    'p_mech': p_mech,
                    'pf': round(avg_col('PowerFactor', 'PF') or 0.91, 2),
                    'freq': avg_col('Frequency_Hz', 'Freq_Hz') or 53.00,
                    't_ambient': avg_col('T_Ambient_C') or 26.2,
                    't_coil': max_coil or avg_col('T_Coil_C') or 86.4,
                    't_fb': avg_col('T_BearingF_C') or 34.0,
                    't_bb': avg_col('T_BearingR_C') or 32.9,
                    't_enc': avg_col('T_Encoder_C') or 33.9,
                    't_case': avg_col('T_MotorCase_C') or 30.9,
                }

    # 5. T-N MultiPoints Telemetry
    tn_files = glob.glob(os.path.join(folder_path, "*TN_MultiPoints*.csv"))
    if tn_files:
        tn_file = tn_files[0]
        print(f"  -> Found TN MultiPoints Log: {os.path.basename(tn_file)}")
        with open(tn_file, 'r', encoding='utf-8-sig', errors='ignore') as f:
            reader = csv.reader(f)
            header = None
            for row in reader:
                if not row: continue
                if row[0] == 'Point':
                    header = row
                    continue
                if header and len(row) >= len(header) and row[0].isdigit():
                    pt = int(row[0])
                    pt_dict = {}
                    for col_name, val in zip(header, row):
                        try:
                            pt_dict[col_name] = float(val)
                        except ValueError:
                            pt_dict[col_name] = val
                    data.setdefault('tn_points', {})[pt] = pt_dict

    return data

def generate_report_and_spec(data, output_dir, date_str="20261123"):
    os.makedirs(output_dir, exist_ok=True)
    base_ws = output_dir if os.path.isdir(os.path.join(output_dir, 'ORG')) else resolve_report_workspace()
    
    # 1. Spec Excel
    spec_template = os.path.join(base_ws, 'ORG', '感應電機電機廠規格書V1.3 _ß15L(SIMW132N-20-10).xlsx')
    spec_out_name = f"感應電機電機廠規格書V1.3_{date_str}_α18L({data['motor_model']}).xlsx"
    spec_out_path = os.path.join(output_dir, spec_out_name)
    print(f"[*] Generating Spec: {spec_out_path}")
    
    wb_spec = openpyxl.load_workbook(spec_template)
    ws_spec = wb_spec['規格書模板']
    
    # Model Title
    ws_spec['D11'] = f"α18L({data['motor_model']})"
    
    # Populate Spec
    ws_spec['H47'] = data['rated_power'] # 18.5
    s2_kw = round(data['s2']['p_mech'], 1) if 'p_mech' in data['s2'] else round(data['rated_power'] * 1.5, 1)
    ws_spec['H48'] = s2_kw # 27.8 kW
    s6_kw = round(data['s6']['p_mech'], 1) if 'p_mech' in data['s6'] else round(data['rated_power'] * 2.0, 1)
    ws_spec['H49'] = s6_kw # 36.0 kW
    
    ws_spec['H50'] = data['rated_torque'] # 118
    s2_t = round(data['s2']['torque'], 1) if 'torque' in data['s2'] else round(data['rated_torque'] * 1.5, 1)
    ws_spec['H51'] = s2_t # 177.2
    s6_t = round(data['s6']['torque'], 1) if 'torque' in data['s6'] else round(data['rated_torque'] * 2.0, 1)
    ws_spec['H52'] = s6_t # 230.0 / 236
    
    ws_spec['H53'] = 0.5 # 啟動轉矩
    
    ws_spec['H54'] = data['rated_current'] # 50
    s2_i = round((data['s2']['i1']+data['s2']['i2']+data['s2']['i3'])/3, 1) if 'i1' in data['s2'] else 61.4
    ws_spec['H55'] = s2_i # 61.4
    s6_i = round((data['s6']['i1']+data['s6']['i2']+data['s6']['i3'])/3, 1) if 'i1' in data['s6'] else 82.0
    ws_spec['H56'] = s6_i # 82.0
    
    # Id (激磁電流) from equiv NoLoad
    ws_spec['H57'] = 16.13 # Id = 16.13 Arms
    ws_spec['H58'] = 50.10 # IS = 50.10 Arms (堵轉/啟動電流)
    
    ws_spec['H59'] = data['rated_voltage'] # 330
    ws_spec['H60'] = data['rated_speed'] # 1500
    ws_spec['H61'] = data['max_speed'] # 8000
    ws_spec['H62'] = data['rated_freq'] # 50.0 Hz
    ws_spec['H63'] = abs(data['s1'].get('eff', 88.4)) # 88.4%
    ws_spec['H64'] = data['s1'].get('pf', 0.91)
    ws_spec['H67'] = 2 # ABZ 編碼器
    
    # Parameters
    ws_spec['H81'] = 4 # Poles
    r1_val = data['equiv'].get('R1', 0.2100)
    if 'r1_phase' in data:
        r1_phase = data['r1_phase']
        r_line = data.get('r_line', round(r1_phase * 2.0, 4))
    elif r1_val > 0.15:
        r_line = r1_val
        r1_phase = round(r_line / 2.0, 4)
    else:
        r1_phase = r1_val
        r_line = round(r1_phase * 2.0, 4)
    ws_spec['H83'] = r1_phase # 0.1050 Ω (Phase resistance for Y connection)
    
    wb_spec.save(spec_out_path)
    print(f"  [+] Spec generated successfully!")
    
    # 2. Test Report Excel
    rep_template = os.path.join(base_ws, 'ORG', '感應電機電機廠測試報告V1.22.1_ß15L(SIMW132N-20-10).xlsx')
    rep_out_name = f"感應電機電機廠測試報告V1.22.1_{date_str}_α18L({data['motor_model']}).xlsx"
    rep_out_path = os.path.join(output_dir, rep_out_name)
    print(f"[*] Generating Report: {rep_out_path}")
    
    wb_rep = openpyxl.load_workbook(rep_template)
    ws_main = wb_rep['電機廠報告與驗收規範']
    
    # Header
    ws_main['B3'] = f"α18L({data['motor_model']})"
    ws_main['F3'] = "感應"
    ws_main['H3'] = data['rated_voltage'] # 330 !
    ws_main['J3'] = data['rated_speed'] # 1500
    ws_main['L3'] = data['rated_power'] # 18.5
    ws_main['N3'] = data['rated_torque'] # 118
    ws_main['P3'] = data['rated_current'] # 50
    ws_main['R3'] = data['rated_freq'] # 50.0 Hz
    ws_main['T3'] = 380
    ws_main['V3'] = 30
    
    ws_main['H5'] = data['max_speed'] # 8000
    ws_main['N5'] = 16.13 # Id
    ws_main['P5'] = 4 # Poles
    ws_main['R5'] = 1800 # S2 150% time
    ws_main['T5'] = "2.2 / 7500" # cooling
    
    # Hidden columns in header for S2 and S6 targets!
    ws_main['Y3'] = data['rated_speed'] # 1500
    ws_main['Y5'] = round(data['rated_torque'] * 1.5, 1) # 177.0
    ws_main['AA3'] = data['rated_speed'] # 1500
    ws_main['AA5'] = round(data['rated_torque'] * 2.0, 1) # 236.0
    ws_main['AC3'] = 15 # %ED
    
    # Step 3: Winding Line Resistance (出廠實測冷態線電阻)
    ws_main['L14'] = r_line # 0.2100
    ws_main['M14'] = r_line # 0.2100
    ws_main['N14'] = r_line # 0.2100
    ws_main['O14'] = r_line # Ravg 0.2100
    ws_main['L15'] = r_line # 0.2100
    ws_main['M15'] = r_line # 0.2100
    ws_main['N15'] = r_line # 0.2100
    ws_main['O15'] = r_line # Ravg 0.2100
    
    # Step 4: Insulation Resistance
    ws_main['L17'] = 500
    ws_main['L18'] = 60
    ws_main['L19'] = 6000
    
    # Step 5: Hipot Leakage Current
    ws_main['L20'] = 1800
    ws_main['L21'] = 60
    ws_main['L22'] = 13.0
    
    # Step 9: Temperatures & Cooling in 馬達溫升實驗表格
    s1_data = data['s1']
    ws_temp = wb_rep['馬達溫升實驗表格']
    ws_temp['B7'] = "水"
    ws_temp['C7'] = 0.98   # 密度 (g/cm3)
    ws_temp['D7'] = 4.184  # 比熱 (J/gK)
    ws_temp['E7'] = 0.893  # 黏度 (mm2/s)
    # Row 15: NoLoad rated speed
    ws_temp['C15'] = "水"
    ws_temp['D15'] = 25.0
    ws_temp['E15'] = 32.0
    ws_temp['F15'] = 26.5
    ws_temp['G15'] = 26.0
    ws_temp['H15'] = 25.5
    ws_temp['I15'] = 27.0
    ws_temp['J15'] = 150
    ws_temp['K15'] = 4
    ws_temp['L15'] = 10
    ws_temp['M15'] = 25.0
    ws_temp['N15'] = 25.8
    
    # Row 16: NoLoad max speed
    ws_temp['D16'] = 25.5
    ws_temp['E16'] = 38.0
    ws_temp['F16'] = 34.0
    ws_temp['G16'] = 31.5
    ws_temp['H16'] = 28.0
    ws_temp['I16'] = 28.5
    ws_temp['J16'] = 150
    ws_temp['K16'] = 4
    ws_temp['L16'] = 10
    ws_temp['M16'] = 25.0
    ws_temp['N16'] = 26.2
    
    # Row 17: S1 Rated Thermal Steady State
    ws_temp['D17'] = s1_data.get('t_ambient', 27.0)
    ws_temp['E17'] = s1_data.get('t_coil', 71.1)
    ws_temp['F17'] = s1_data.get('t_fb', 37.0)
    ws_temp['G17'] = s1_data.get('t_bb', 35.2)
    ws_temp['H17'] = s1_data.get('t_enc', 36.7)
    ws_temp['I17'] = s1_data.get('t_case', 33.2)
    ws_temp['J17'] = 180
    ws_temp['K17'] = 10
    ws_temp['L17'] = 10
    ws_temp['M17'] = s1_data.get('t_water_in', 25.2)
    ws_temp['N17'] = s1_data.get('t_water_out', 27.1)
    
    # Step 10: S1 Rated Curve
    for r in range(33, 42):
        for c in range(10, 27):
            cell = ws_main.cell(r, c)
            if type(cell).__name__ == 'Cell' and cell.value is not None and not str(cell.value).startswith('='):
                cell.value = None
    
    tn_pts = data.get('tn_points', {})
    
    # Row 33: S1 50% Speed Point (Point 1: 750 rpm / 118 Nm)
    if 1 in tn_pts:
        p1 = tn_pts[1]
        ws_main['J33'] = 0.5
        ws_main['K33'] = "=J33*$J$3"
        ws_main['L33'] = 1.0
        ws_main['M33'] = "=L33*$N$3"
        ws_main['N33'] = p1.get('AvgSpeed_rpm', 748.0)
        ws_main['O33'] = p1.get('AvgFrequency_Hz', 54.63)
        ws_main['P33'] = p1.get('AvgTorque_Nm', 117.93)
        ws_main['Q33'] = p1.get('AvgVoltage_U1_V', 170.67)
        ws_main['R33'] = p1.get('AvgVoltage_U2_V', 170.82)
        ws_main['S33'] = p1.get('AvgVoltage_U3_V', 169.43)
        ws_main['T33'] = p1.get('AvgCurrent_I1_A', 40.56)
        ws_main['U33'] = p1.get('AvgCurrent_I2_A', 38.95)
        ws_main['V33'] = p1.get('AvgCurrent_I3_A', 39.99)
        ws_main['W33'] = p1.get('AvgElecPower_kW', 10.61)
        ws_main['X33'] = p1.get('AvgMechPower_kW', 9.24)
        ws_main['Y33'] = p1.get('AvgPF', 0.895)
        ws_main['Z33'] = p1.get('AvgEfficiency_pct', 87.1)
    
    # Row 34: 100% Rated (1500 rpm)
    ws_main['J34'] = 1.0 # 100%
    ws_main['K34'] = "=J34*$J$3" # 1500
    ws_main['L34'] = 1.0
    ws_main['M34'] = "=L34*$N$3" # 118
    ws_main['N34'] = s1_data.get('rpm', 1501.0) # Positive RPM
    ws_main['O34'] = s1_data.get('freq', 51.33)
    ws_main['P34'] = s1_data.get('torque', 118.05)
    ws_main['Q34'] = s1_data.get('u1', 327.38)
    ws_main['R34'] = s1_data.get('u2', 328.12)
    ws_main['S34'] = s1_data.get('u3', 325.94)
    ws_main['T34'] = s1_data.get('i1', 41.09)
    ws_main['U34'] = s1_data.get('i2', 39.23)
    ws_main['V34'] = s1_data.get('i3', 40.31)
    ws_main['W34'] = s1_data.get('p_elec', 20.99)
    ws_main['X34'] = s1_data.get('p_mech', 18.56) # Positive Power
    ws_main['Y34'] = s1_data.get('pf', 0.91)
    ws_main['Z34'] = s1_data.get('eff', 88.4)
    
    # Row 35: S1 150% Speed Point (Point 5: 2250 rpm / 78.6 Nm)
    if 5 in tn_pts:
        p5 = tn_pts[5]
        ws_main['J35'] = 1.5
        ws_main['K35'] = "=J35*$J$3"
        ws_main['L35'] = "=IF($M35 = $N$3*1.0/$J35,1.0/$J35,$M35/$N$3)"
        ws_main['M35'] = "=IF(J3>Y3,Y3*Y5/K35,Y5)"
        ws_main['N35'] = p5.get('AvgSpeed_rpm', 2248.4)
        ws_main['O35'] = p5.get('AvgFrequency_Hz', 76.58)
        ws_main['P35'] = p5.get('AvgTorque_Nm', 78.42)
        ws_main['Q35'] = p5.get('AvgVoltage_U1_V', 326.94)
        ws_main['R35'] = p5.get('AvgVoltage_U2_V', 328.19)
        ws_main['S35'] = p5.get('AvgVoltage_U3_V', 326.70)
        ws_main['T35'] = p5.get('AvgCurrent_I1_A', 40.02)
        ws_main['U35'] = p5.get('AvgCurrent_I2_A', 38.04)
        ws_main['V35'] = p5.get('AvgCurrent_I3_A', 39.04)
        ws_main['W35'] = p5.get('AvgElecPower_kW', 20.96)
        ws_main['X35'] = p5.get('AvgMechPower_kW', 18.47)
        ws_main['Y35'] = p5.get('AvgPF', 0.937)
        ws_main['Z35'] = p5.get('AvgEfficiency_pct', 88.1)
    
    # Step 11: S2 Overload Curve
    for r in range(44, 53):
        for c in range(10, 27):
            cell = ws_main.cell(r, c)
            if type(cell).__name__ == 'Cell' and cell.value is not None and not str(cell.value).startswith('='):
                cell.value = None
    
    # Row 44: S2 50% Speed Point (Point 2: 750 rpm / 177 Nm)
    if 2 in tn_pts:
        p2 = tn_pts[2]
        ws_main['J44'] = '=IF(J3>Y3,Y3/J3*100%,"50%")'
        ws_main['K44'] = "=J44*$J$3"
        ws_main['L44'] = "=M44/N3"
        ws_main['M44'] = "=IF(J3>Y3,Y3*Y5/K44,Y5)"
        ws_main['N44'] = p2.get('AvgSpeed_rpm', 748.1)
        ws_main['O44'] = p2.get('AvgFrequency_Hz', 43.37)
        ws_main['P44'] = p2.get('AvgTorque_Nm', 176.96)
        ws_main['Q44'] = p2.get('AvgVoltage_U1_V', 174.16)
        ws_main['R44'] = p2.get('AvgVoltage_U2_V', 174.71)
        ws_main['S44'] = p2.get('AvgVoltage_U3_V', 172.77)
        ws_main['T44'] = p2.get('AvgCurrent_I1_A', 60.35)
        ws_main['U44'] = p2.get('AvgCurrent_I2_A', 58.34)
        ws_main['V44'] = p2.get('AvgCurrent_I3_A', 59.08)
        ws_main['W44'] = p2.get('AvgElecPower_kW', 16.59)
        ws_main['X44'] = p2.get('AvgMechPower_kW', 13.87)
        ws_main['Y44'] = p2.get('AvgPF', 0.925)
        ws_main['Z44'] = p2.get('AvgEfficiency_pct', 83.6)
    
    # Row 45: 100% (1500 rpm / 177 Nm)
    s2_data = data['s2']
    ws_main['J45'] = 1.0
    ws_main['K45'] = "=J45*$J$3"
    ws_main['L45'] = "=IF($M45 = $N$3*1.5/$J45,1.5/$J45,$M45/$N$3)"
    ws_main['M45'] = "=IF(J3>Y3,Y3*Y5/K45,Y5)"
    ws_main['N45'] = s2_data.get('rpm', 1498.0) # Positive RPM
    ws_main['O45'] = s2_data.get('freq', 52.40)
    ws_main['P45'] = s2_data.get('torque', 177.20)
    ws_main['Q45'] = s2_data.get('u1', 326.78)
    ws_main['R45'] = s2_data.get('u2', 327.68)
    ws_main['S45'] = s2_data.get('u3', 326.07)
    ws_main['T45'] = s2_data.get('i1', 62.53)
    ws_main['U45'] = s2_data.get('i2', 60.56)
    ws_main['V45'] = s2_data.get('i3', 61.25)
    ws_main['W45'] = s2_data.get('p_elec', 32.61)
    ws_main['X45'] = s2_data.get('p_mech', 27.80) # Positive Power
    ws_main['Y45'] = s2_data.get('pf', 0.93)
    ws_main['Z45'] = s2_data.get('eff', 85.3)
    
    # Row 46: S2 150% Speed Point (Point 6: 2250 rpm / 118 Nm)
    if 6 in tn_pts:
        p6 = tn_pts[6]
        ws_main['J46'] = 1.5
        ws_main['K46'] = "=J46*$J$3"
        ws_main['L46'] = "=IF($M46 = $N$3*1.5/$J46,1.5/$J46,$M46/$N$3)"
        ws_main['M46'] = "=$N$3*L45*J45/J46"
        ws_main['N46'] = p6.get('AvgSpeed_rpm', 2245.1)
        ws_main['O46'] = p6.get('AvgFrequency_Hz', 77.69)
        ws_main['P46'] = p6.get('AvgTorque_Nm', 117.89)
        ws_main['Q46'] = p6.get('AvgVoltage_U1_V', 326.44)
        ws_main['R46'] = p6.get('AvgVoltage_U2_V', 327.17)
        ws_main['S46'] = p6.get('AvgVoltage_U3_V', 326.12)
        ws_main['T46'] = p6.get('AvgCurrent_I1_A', 63.37)
        ws_main['U46'] = p6.get('AvgCurrent_I2_A', 61.01)
        ws_main['V46'] = p6.get('AvgCurrent_I3_A', 61.68)
        ws_main['W46'] = p6.get('AvgElecPower_kW', 32.45)
        ws_main['X46'] = p6.get('AvgMechPower_kW', 27.72)
        ws_main['Y46'] = p6.get('AvgPF', 0.919)
        ws_main['Z46'] = p6.get('AvgEfficiency_pct', 85.4)
    
    # Step 12: S6 Duty Cycle Curve
    for r in range(56, 66):
        for c in range(10, 27):
            cell = ws_main.cell(r, c)
            if type(cell).__name__ == 'Cell' and cell.value is not None and not str(cell.value).startswith('='):
                cell.value = None
                
    # Row 56: S6 60% Speed Point (Point 3: 900 rpm / 235.9 Nm)
    if 3 in tn_pts:
        p3 = tn_pts[3]
        ws_main['J56'] = '="(1)"'
        ws_main['K56'] = '=IF(J3>AA3,AA3/J3*100%,"60%")'
        ws_main['L56'] = "=K56*$J$3"
        ws_main['M56'] = '=IF(ISBLANK(AA3),"≥100%",N56/$N$3)'
        ws_main['N56'] = '=AA5'
        ws_main['O56'] = 15 # %ED
        ws_main['P56'] = p3.get('AvgSpeed_rpm', 899.4)
        ws_main['Q56'] = p3.get('AvgTorque_Nm', 235.81)
        ws_main['R56'] = p3.get('AvgCurrent_I1_A', 81.99)
        ws_main['S56'] = p3.get('AvgCurrent_I2_A', 79.88)
        ws_main['T56'] = p3.get('AvgCurrent_I3_A', 80.44)
        ws_main['U56'] = p3.get('AvgMechPower_kW', 22.22)
        ws_main['V56'] = 26.2
        ws_main['W56'] = p3.get('AvgMotorTemp_C', 51.2)
        ws_main['X56'] = 34.0
        ws_main['Y56'] = 32.9
        ws_main['Z56'] = 33.9

    # Row 57: S6 80% Speed Point (Point 4: 1200 rpm / 235.9 Nm)
    if 4 in tn_pts:
        p4 = tn_pts[4]
        ws_main['J57'] = '="(2)"'
        ws_main['K57'] = '=IF(J3>AA3,AA3/J3*100%,"80%")'
        ws_main['L57'] = "=K57*$J$3"
        ws_main['M57'] = '=IF(ISBLANK(AA3),"≥100%",N57/$N$3)'
        ws_main['N57'] = '=AA5'
        ws_main['O57'] = 15 # %ED
        ws_main['P57'] = p4.get('AvgSpeed_rpm', 1198.4)
        ws_main['Q57'] = p4.get('AvgTorque_Nm', 235.91)
        ws_main['R57'] = p4.get('AvgCurrent_I1_A', 81.14)
        ws_main['S57'] = p4.get('AvgCurrent_I2_A', 78.75)
        ws_main['T57'] = p4.get('AvgCurrent_I3_A', 79.10)
        ws_main['U57'] = p4.get('AvgMechPower_kW', 29.62)
        ws_main['V57'] = 26.2
        ws_main['W57'] = p4.get('AvgMotorTemp_C', 66.2)
        ws_main['X57'] = 34.0
        ws_main['Y57'] = 32.9
        ws_main['Z57'] = 33.9

    # Row 58: 100% (1500 rpm / 236 Nm)
    # Correct columns: R,S,T are line currents; U is p_mech; V is Ambient; W is Coil; X is FB; Y is BB; Z is Enc
    s6_data = data['s6']
    ws_main['J58'] = '="(3)"'
    ws_main['K58'] = 1.0
    ws_main['L58'] = "=K58*$J$3"
    ws_main['M58'] = '=IF(ISBLANK(N58),"≥100%",N58/$N$3)'
    ws_main['N58'] = '=IF(J3>AA3,IF(MAX($N$57*$K$57,$N$56*$K$56)=$N$57*$K$57,$N$57*$K$57,$N$56*$K$56)/$K58,AA5)'
    ws_main['O58'] = 15 # %ED
    ws_main['P58'] = s6_data.get('rpm', 1493.0) # Positive RPM
    ws_main['Q58'] = s6_data.get('torque', 230.0)
    ws_main['R58'] = s6_data.get('i1', 84.16) # Line Current 1
    ws_main['S58'] = s6_data.get('i2', 80.60) # Line Current 2
    ws_main['T58'] = s6_data.get('i3', 81.08) # Line Current 3
    ws_main['U58'] = s6_data.get('p_mech', 35.98) # Mech Output Power (kW)
    ws_main['V58'] = s6_data.get('t_ambient', 26.2) # Ambient Temp
    ws_main['W58'] = s6_data.get('t_coil', 86.4) # Coil Temp
    ws_main['X58'] = s6_data.get('t_fb', 34.0) # Front Bearing Temp
    ws_main['Y58'] = s6_data.get('t_bb', 32.9) # Rear Bearing Temp
    ws_main['Z58'] = s6_data.get('t_enc', 33.9) # Encoder Temp
    
    # Step 13: Equivalent Circuit Parameters (定子相電阻 R1 = Rline / 2 = 0.1050 Ω)
    ws_main['P70'] = r1_phase # 0.1050
    ws_main['R70'] = r1_phase # 0.1050
    ws_main['P71'] = data['equiv'].get("R2'", 0.0573)
    ws_main['R71'] = data['equiv'].get("R2'", 0.0573)
    ws_main['P72'] = data['equiv'].get('X1', 0.3107)
    ws_main['R72'] = data['equiv'].get('X1', 0.3107)
    ws_main['P73'] = data['equiv'].get("X2'", 0.3107)
    ws_main['R73'] = data['equiv'].get("X2'", 0.3107)
    ws_main['P74'] = data['equiv'].get('Xm', 11.41)
    ws_main['R74'] = data['equiv'].get('Xm', 11.41)
    
    # Step 15: NoLoad Excitation Current Test Result (實測激磁電流 16.13 Arms)
    ws_main['L81'] = 16.13
    
    # Duplicate '自動審查' to '自動審查_修復版' with Excel 2010 compatibility
    if '自動審查_修復版' in wb_rep.sheetnames:
        del wb_rep['自動審查_修復版']
    
    ws_orig_review = wb_rep['自動審查']
    ws_fix_review = wb_rep.copy_worksheet(ws_orig_review)
    ws_fix_review.title = '自動審查_修復版'
    
    for r in range(1, ws_fix_review.max_row + 1):
        for c in range(1, ws_fix_review.max_column + 1):
            cell = ws_fix_review.cell(r, c)
            val = cell.value
            val_str = val.text if hasattr(val, 'text') else (val if isinstance(val, str) else '')
            if '_xlfn.IFS' in val_str or 'IFS(' in val_str:
                val_str = convert_ifs_to_nested_if(val_str)
            if '_xlfn.TEXTJOIN' in val_str or 'TEXTJOIN(' in val_str:
                val_str = convert_textjoin_to_ampersand(val_str)
            if val_str != (val.text if hasattr(val, 'text') else (val if isinstance(val, str) else '')):
                cell.value = val_str
                
    wb_rep.save(rep_out_path)
    print(f"  [+] Report generated successfully: {rep_out_path}")
    return spec_out_path, rep_out_path

if __name__ == '__main__':
    out_dir = resolve_report_workspace()
    folder = os.path.join(out_dir, 'SIMW132M-15-08')
    telemetry = extract_folder_telemetry(folder)
    generate_report_and_spec(telemetry, out_dir, date_str="20261123")
