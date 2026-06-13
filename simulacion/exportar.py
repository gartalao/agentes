"""
Exporta a JSON el MODELO de control que Unity ejecuta en lazo cerrado.

NO exporta tiempos preprogramados: exporta (a) los offsets de coordinacion y
(b) la POLITICA aprendida por Q-learning (estado discretizado -> verde del
corredor). Unity carga esto y, en cada ciclo, SENSA las colas de sus detectores,
discretiza el estado y elige la accion de la politica. Asi el controlador
adaptativo decide en vivo segun lo que mide, igual que un sistema real.
"""
import json

from . import corredor as C
from . import control as ctrl
from . import microsim


def construir_plan(policy_ser, resultados=None):
    plan = {
        "corredor": {
            "nombre": "Av. Gomez Morin, San Pedro Garza Garcia, N.L.",
            "velocidad_sincronia_kmh": C.V_KMH,
            "ciclo_s": C.CYCLE,
            "verde_base_s": C.GREEN,
            "amarillo_s": C.YELLOW,
            "todo_rojo_s": C.ALLRED,
            "verde_repartible_s": round(microsim.usable(), 1),
            "cruces": [
                {"id": c, "nombre": C.NOMBRES[c], "dist_m": C.DIST[c]}
                for c in C.ORDER
            ],
        },
        "escenarios": {
            "sin_coord": {
                "descripcion": "Tiempo fijo, semaforos aislados (sin coordinacion).",
                "tipo": "fijo",
                "offsets_s": ctrl.offsets_por_escenario("sin_coord"),
                "verde_corredor_s": C.GREEN,
            },
            "onda_verde": {
                "descripcion": "Tiempo fijo coordinado por offsets = distancia / velocidad.",
                "tipo": "fijo_coordinado",
                "offsets_s": ctrl.offsets_por_escenario("onda_verde"),
                "verde_corredor_s": C.GREEN,
            },
            "adaptativo": {
                "descripcion": "Lazo cerrado: sensa colas y elige verde con politica Q-learning.",
                "tipo": "adaptativo",
                "offsets_s": ctrl.offsets_por_escenario("onda_verde"),
            },
        },
        # politica que Unity ejecuta en vivo
        "politica_adaptativa": {
            "acciones_verde_corredor_s": ctrl.GREEN_OPTIONS,
            "umbrales_cola": [0, 3, 8],   # buckets: 0 | 1-3 | 4-8 | >8
            "alcance_detector_m": microsim.DET_ZONE,
            "Q": policy_ser,              # {cid: {"b_cor,b_tr": [Q por accion]}}
        },
    }
    # bloque con forma de arreglos para que Unity lo lea con JsonUtility
    off_ov = ctrl.offsets_por_escenario("onda_verde")
    off_sc = ctrl.offsets_por_escenario("sin_coord")
    def _offlist(off):
        return [{"id": c, "off": round(off[c], 2)} for c in C.ORDER]
    greedy = []
    for cid, tabla in policy_ser.items():
        for clave, qs in tabla.items():
            bc, bt = (int(x) for x in clave.split(","))
            ai = max(range(len(qs)), key=lambda k: qs[k])
            greedy.append({"cid": cid, "bc": bc, "bt": bt,
                           "verde": ctrl.GREEN_OPTIONS[ai]})
    plan["unity"] = {
        "escenarios": [
            {"nombre": "sin_coord", "tipo": "fijo", "offsets": _offlist(off_sc), "verde": C.GREEN},
            {"nombre": "onda_verde", "tipo": "fijo_coordinado", "offsets": _offlist(off_ov), "verde": C.GREEN},
            {"nombre": "adaptativo", "tipo": "adaptativo", "offsets": _offlist(off_ov), "verde": C.GREEN},
        ],
        "acciones_verde_s": ctrl.GREEN_OPTIONS,
        "umbrales_cola": [0, 3, 8],
        "detector_m": microsim.DET_ZONE,
        "politica": greedy,
    }
    if resultados is not None:
        plan["resultados_referencia"] = resultados
    return plan


def escribir(plan, ruta):
    with open(ruta, "w", encoding="utf-8") as f:
        json.dump(plan, f, ensure_ascii=False, indent=2)
    return ruta
