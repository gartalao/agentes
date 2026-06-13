"""
Corre todo el estudio y regenera resultados:
  - entrena el control adaptativo (Q-learning)
  - simula los 3 escenarios (demanda nominal y alta)
  - calcula metricas, ancho de banda e indice de coordinacion
  - genera TODAS las graficas (empiricas)
  - exporta plan_control.json para Unity y un CSV de metricas

Uso:  python -m simulacion.main      (desde la raiz del repo)
"""
import os
import csv

from . import corredor as C
from . import control, metricas, graficas, exportar
from .microsim import CorredorModel

LAMBDA_TR = {"C3": 0.12, "C2": 0.18, "C1": 0.14}


def _correr(modo, policy=None, lam=C.DEMANDA["base"], steps=1600, seed=7):
    esc = "sin_coord" if modo == "sin_coord" else "onda_verde"
    offs = control.offsets_por_escenario(esc)
    p = {"modo": modo, "offsets": offs, "lambda_in": lam, "policy": policy,
         "seed": seed, "lambda_tr": LAMBDA_TR}
    m = CorredorModel(p)
    m.run(steps=steps, display=False)
    return m


def correr_estudio(outdir):
    os.makedirs(outdir, exist_ok=True)

    # 1) entrenar control adaptativo
    policy, policy_ser, hist = control.entrenar_qlearning(episodios=500, pasos=80)

    # 2) simular los 3 escenarios (nominal y alta demanda)
    modelos, resultados = {}, {}
    for modo, pol in [("sin_coord", None), ("onda_verde", None), ("adaptativo", policy)]:
        m = _correr(modo, pol, lam=C.DEMANDA["base"])
        modelos[modo] = m
        resultados[modo] = metricas.resumen(m)

    resultados_alta = {}
    for modo, pol in [("sin_coord", None), ("onda_verde", None), ("adaptativo", policy)]:
        m = _correr(modo, pol, lam=C.DEMANDA["alta"])
        resultados_alta[modo] = metricas.resumen(m)

    # 3) ancho de banda por escenario
    bw = {}
    for esc in ("sin_coord", "onda_verde"):
        offs = control.offsets_por_escenario(esc)
        b, e = metricas.ancho_de_banda(offs)
        bw[esc] = {"ancho_s": b, "eficiencia_pct": e}

    # 4) graficas
    g = lambda n: os.path.join(outdir, n)
    nombres = {"sin_coord": "Escenario 1: sin coordinacion",
               "onda_verde": "Escenario 2: onda verde (tiempo fijo coordinado)",
               "adaptativo": "Escenario 3: adaptativo multiagente (Q-learning)"}
    for modo, m in modelos.items():
        offs = control.offsets_por_escenario("sin_coord" if modo == "sin_coord" else "onda_verde")
        graficas.diagrama_espacio_tiempo(
            m, offs, f"Diagrama tiempo-espacio | {nombres[modo]}",
            g(f"tiempo_espacio_{modo}.png"))
    graficas.comparacion_escenarios(resultados, g("comparacion_escenarios.png"))
    graficas.colas_tiempo(modelos, g("colas_tiempo.png"))
    graficas.convergencia_qlearning(hist, g("convergencia_qlearning.png"))
    graficas.sensibilidad_velocidad(g("sensibilidad_velocidad.png"))

    # 5) export Unity + CSV
    plan = exportar.construir_plan(policy_ser, resultados={
        "nominal": resultados, "alta_demanda": resultados_alta, "ancho_banda": bw})
    return modelos, resultados, resultados_alta, bw, hist, plan


def guardar_csv(resultados, ruta):
    keys = ["demora_prom_s", "velocidad_kmh", "pct_detenido", "paradas_por_veh",
            "throughput_veh_h", "indice_coordinacion"]
    with open(ruta, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["metrica", "sin_coord", "onda_verde", "adaptativo"])
        for k in keys:
            w.writerow([k] + [resultados[e][k] for e in ("sin_coord", "onda_verde", "adaptativo")])


if __name__ == "__main__":
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    outdir = os.path.join(raiz, "documentacion", "resultados")
    modelos, res, res_alta, bw, hist, plan = correr_estudio(outdir)
    guardar_csv(res, os.path.join(outdir, "metricas_nominal.csv"))
    guardar_csv(res_alta, os.path.join(outdir, "metricas_alta_demanda.csv"))
    # plan_control.json va a Unity (Resources) y al repo
    unity_res = os.path.join(raiz, "unity", "Assets", "Resources", "plan_control.json")
    exportar.escribir(plan, os.path.join(outdir, "plan_control.json"))
    if os.path.isdir(os.path.dirname(unity_res)):
        exportar.escribir(plan, unity_res)
    print("OK. Resultados en", outdir)
    for esc in ("sin_coord", "onda_verde", "adaptativo"):
        r = res[esc]
        print(f"  {esc:11s} demora={r['demora_prom_s']:6.1f}s vel={r['velocidad_kmh']:.1f} "
              f"paradas={r['paradas_por_veh']:.2f} coord={r['indice_coordinacion']:.0f}%")
    print("  ancho de banda onda verde:", bw["onda_verde"])
