from http.server import BaseHTTPRequestHandler, HTTPServer
from datetime import datetime, timezone
import json


class AnalysisHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        # Recebe pedidos RPC de análise do Servidor.
        if self.path != "/rpc/analyze":
            self.send_response(404)
            self.end_headers()
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
            result = analyze(payload)
            self.write_json(result, 200)
        except Exception as exc:
            self.write_json({"ok": False, "error": str(exc)}, 500)

    def log_message(self, format, *args):
        return

    def write_json(self, value, status):
        data = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def analyze(payload):
    # Calcula estatísticas simples sobre as leituras recebidas.
    readings = payload.get("readings", [])
    values = [float(read_value(reading, "valor")) for reading in readings]

    if not values:
        return {
            "ok": True,
            "count": 0,
            "risk": "sem_dados",
            "message": "Não existem leituras para os filtros indicados.",
            "generatedAtUtc": now_utc(),
        }

    average = sum(values) / len(values)
    minimum = min(values)
    maximum = max(values)
    sensor_type = (payload.get("tipo") or infer_type(readings)).upper()

    return {
        "ok": True,
        "sensorId": payload.get("sensorId"),
        "zona": payload.get("zona"),
        "tipo": sensor_type,
        "fromUtc": payload.get("fromUtc"),
        "toUtc": payload.get("toUtc"),
        "count": len(values),
        "average": round(average, 2),
        "min": minimum,
        "max": maximum,
        "risk": classify_risk(sensor_type, average, maximum),
        "generatedAtUtc": now_utc(),
    }


def infer_type(readings):
    if not readings:
        return "DESCONHECIDO"
    return read_value(readings[0], "tipo", "DESCONHECIDO")


def read_value(reading, name, default=None):
    if name in reading:
        return reading[name]

    pascal_name = name[:1].upper() + name[1:]
    if pascal_name in reading:
        return reading[pascal_name]

    return default


def classify_risk(sensor_type, average, maximum):
    # Classifica o risco com base no tipo de sensor e nos valores.
    if sensor_type == "PM25":
        if maximum >= 75 or average >= 50:
            return "alto"
        if maximum >= 35 or average >= 25:
            return "moderado"
        return "baixo"

    if sensor_type == "TEMP":
        if maximum >= 35:
            return "alto"
        if maximum >= 30:
            return "moderado"
        return "baixo"

    return "baixo"


def now_utc():
    return datetime.now(timezone.utc).isoformat()


if __name__ == "__main__":
    server = HTTPServer(("localhost", 7002), AnalysisHandler)
    print("Serviço RPC de análise iniciado em http://localhost:7002/rpc/analyze")
    server.serve_forever()
