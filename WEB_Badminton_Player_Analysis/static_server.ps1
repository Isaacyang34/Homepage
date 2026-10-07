$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('http://127.0.0.1:8080/')
try { $listener.Start() } catch {
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add('http://127.0.0.1:8888/')
    $listener.Start()
}
Write-Output "Server running on $($listener.Prefixes)"
$root = 'c:\Users\peter\OneDrive\Desktop\AI_Projects\WEB_Badminton_Player_Analysis'

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $req = $context.Request
        $res = $context.Response
        
        $res.AddHeader('Access-Control-Allow-Origin', '*')
        $res.AddHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        $res.AddHeader('Access-Control-Allow-Headers', '*')
        
        if ($req.HttpMethod -eq 'OPTIONS') {
            $res.StatusCode = 204
            $res.OutputStream.Close()
            continue
        }

        $localPath = $req.Url.LocalPath.TrimStart('/')
        if ([string]::IsNullOrEmpty($localPath)) { $localPath = 'index.html' }
        $filePath = Join-Path $root $localPath
        
        if (Test-Path $filePath -PathType Leaf) {
            $bytes = [System.IO.File]::ReadAllBytes($filePath)
            $res.AddHeader('Accept-Ranges', 'bytes')

            if ($filePath.EndsWith('.html')) { $res.ContentType = 'text/html; charset=utf-8' }
            elseif ($filePath.EndsWith('.js')) { $res.ContentType = 'application/javascript; charset=utf-8' }
            elseif ($filePath.EndsWith('.css')) { $res.ContentType = 'text/css; charset=utf-8' }
            elseif ($filePath.EndsWith('.mp4')) { $res.ContentType = 'video/mp4' }
            elseif ($filePath.EndsWith('.png')) { $res.ContentType = 'image/png' }
            elseif ($filePath.EndsWith('.json')) { $res.ContentType = 'application/json; charset=utf-8' }

            $rangeHeader = $req.Headers['Range']
            if ($rangeHeader -and $rangeHeader.StartsWith('bytes=')) {
                $rangeParts = $rangeHeader.Substring(6).Split('-')
                $start = [long]$rangeParts[0]
                $end = if (![string]::IsNullOrEmpty($rangeParts[1])) { [long]$rangeParts[1] } else { $bytes.Length - 1 }
                if ($end -ge $bytes.Length) { $end = $bytes.Length - 1 }
                $len = $end - $start + 1

                $res.StatusCode = 206
                $res.AddHeader('Content-Range', "bytes $start-$end/$($bytes.Length)")
                $res.ContentLength64 = $len
                $res.OutputStream.Write($bytes, [int]$start, [int]$len)
            } else {
                $res.StatusCode = 200
                $res.ContentLength64 = $bytes.Length
                $res.OutputStream.Write($bytes, 0, $bytes.Length)
            }
        } else {
            $res.StatusCode = 404
        }
        $res.OutputStream.Close()
    } catch {}
}
