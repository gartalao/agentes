"""
Metricas de trafico calculadas a partir de la microsimulacion, y el calculo
analitico del ancho de banda (bandwidth) de la onda verde.
"""
import numpy as np

from . import corredor as C


def resumen(model):
    """Resumen de metricas de una corrida del CorredorModel."""
    delays = np.array(model.delays) if model.delays else np.array([0.0])
    dist = C.S_SALIDA - C.S_ENTRADA
    free_t = dist / C.V_SYNC
    travel = free_t + delays                       # tiempo real de recorrido
    v_prom = dist / travel.mean() * 3.6            # km/h
    pct_det = 100.0 * model.veh_detenidos / max(1, model.veh_muestras)
    paradas = np.array(model.paradas_veh) if model.paradas_veh else np.array([0.0])
    coord = model.cruces_verde / max(1, model.cruces_total)
    # colas
    qcor = {c: float(np.mean(model.qcor_hist[c])) for c in C.ORDER}
    qtr = {c: float(np.mean(model.qtr_hist[c])) for c in C.ORDER}
    qmax = {c: float(np.max(model.qcor_hist[c] + model.qtr_hist[c])) for c in C.ORDER}
    horas = (model.t_now / 3600.0) if model.t_now > 0 else 1.0
    return {
        "demora_prom_s": round(float(delays.mean()), 1),
        "velocidad_kmh": round(float(v_prom), 1),
        "pct_detenido": round(float(pct_det), 1),
        "paradas_por_veh": round(float(paradas.mean()), 2),
        "throughput_veh_h": round(model.salidas / horas, 0),
        "vehiculos_salidos": int(model.salidas),
        "indice_coordinacion": round(float(coord) * 100.0, 0),
        "cola_prom_corredor": {c: round(qcor[c], 1) for c in C.ORDER},
        "cola_prom_transversal": {c: round(qtr[c], 1) for c in C.ORDER},
        "cola_max": {c: round(qmax[c], 0) for c in C.ORDER},
    }


def ancho_de_banda(offsets, v=C.V_SYNC, cycle=C.CYCLE, green=C.GREEN):
    """
    Ancho de banda de la onda verde (s): mayor ventana de salida en C3 tal que
    un vehiculo a velocidad de sincronia alcanza VERDE en los 3 cruces.

    Se barre el instante de salida sobre un ciclo y se mide la corrida continua
    mas larga en la que las 3 llegadas caen en verde.
    """
    d = [C.DIST[c] for c in C.ORDER]
    offs = [offsets[c] for c in C.ORDER]
    t_arr = [d[i] / v for i in range(len(d))]
    paso = 0.1
    mejor = 0.0
    actual = 0.0
    t = 0.0
    while t < cycle:
        ok = True
        for i in range(len(d)):
            local = (t + t_arr[i] - offs[i]) % cycle
            if not (0.0 <= local < green):
                ok = False
                break
        if ok:
            actual += paso
            mejor = max(mejor, actual)
        else:
            actual = 0.0
        t += paso
    # eficiencia de banda = ancho / verde
    return round(mejor, 1), round(100.0 * mejor / green, 0)
