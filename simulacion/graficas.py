"""
Generacion de TODAS las graficas, EMPIRICAS (a partir de la simulacion).

La grafica central es el diagrama espacio-tiempo: bandas verde/rojo de cada
semaforo sobre el eje del corredor + las TRAYECTORIAS REALES de los vehiculos
simulados. Asi se ve la onda verde de verdad (el peloton montado sobre el verde)
en vez de una linea idealizada.
"""
import os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

from . import corredor as C

VERDE = "#27AE60"
ROJO = "#C0392B"
AZUL = "#2E86DE"
NARANJA = "#E67E22"


def _bandas(ax, offsets, horizonte, t0=0.0, green=C.GREEN, cycle=C.CYCLE):
    """Bandas verde/rojo de cada cruce: el verde de cada semaforo arranca en su
    OFFSET dentro del ciclo (por eso la onda se escalona). Diagrama clasico."""
    for cid in C.ORDER:
        d = C.DIST[cid]
        off = offsets[cid]
        k0 = int((t0 - off) // cycle) - 1
        for k in range(k0, k0 + int((horizonte - t0) / cycle) + 3):
            gi = off + k * cycle
            gf = gi + green
            cf = gi + cycle
            ax.hlines(d, gi, gf, color=VERDE, lw=7, zorder=2)
            ax.hlines(d, gf, cf, color=ROJO, lw=7, zorder=2)
        ax.text(horizonte + horizonte * 0.01, d, f"{cid}\n{C.NOMBRES[cid]}",
                va="center", fontsize=9, fontweight="bold")


def diagrama_espacio_tiempo(model, offsets, titulo, fname, ventana=(120, 380)):
    """
    Diagrama tiempo-espacio de onda verde: bandas verde/rojo reales de cada
    semaforo + trayectorias RECTAS del peloton a la velocidad de sincronia
    (un vehiculo lanzado cada `headway` segundos). Es el diagrama clasico: en
    onda verde/adaptativo las rectas pasan por el verde; sin coordinacion las
    rectas chocan con los rojos. Lineas rectas = velocidad constante.
    """
    t0, t1 = ventana
    fig, ax = plt.subplots(figsize=(11, 6))
    _bandas(ax, offsets, t1, t0=t0)
    d0, d1 = 0.0, C.DIST["C1"]              # tramo señalizado del corredor
    viaje = (d1 - d0) / C.V_SYNC
    off3 = offsets[C.ORDER[0]]               # offset de C3 (cabeza del corredor)
    verdes = total = 0
    # el peloton se libera durante el VERDE de C3 (varios vehiculos cada 2 s) y
    # viaja a velocidad de sincronia: rectas que deberian "montar" la onda verde
    k0 = int((t0 - viaje - off3) // C.CYCLE) - 1
    for k in range(k0, k0 + int((t1 - t0) / C.CYCLE) + 4):
        for dt in range(0, int(C.GREEN), 2):
            t_launch = off3 + k * C.CYCLE + dt
            if t_launch + viaje < t0 or t_launch > t1:
                continue
            ax.plot([t_launch, t_launch + viaje], [d0, d1],
                    color=AZUL, lw=1.3, alpha=0.85, zorder=3)
            for cid in C.ORDER:
                ta = t_launch + C.DIST[cid] / C.V_SYNC
                local = (ta - offsets[cid]) % C.CYCLE
                total += 1
                if 0.0 <= local < C.GREEN:
                    verdes += 1
    idx = round(100.0 * verdes / max(1, total))
    ax.set_xlim(t0, t1)
    ax.set_ylim(d0 - 40, d1 + 120)
    ax.set_xlabel("tiempo (s)")
    ax.set_ylabel("distancia sobre el corredor (m)")
    ax.set_title(titulo)
    ax.grid(True, alpha=0.3)
    fig.tight_layout()
    fig.savefig(fname, dpi=120)
    plt.close(fig)
    return idx


def comparacion_escenarios(res, fname):
    """Barras agrupadas de las metricas clave para los 3 escenarios."""
    escs = ["sin_coord", "onda_verde", "adaptativo"]
    etiquetas = ["Sin coordinacion", "Onda verde", "Adaptativo (Q-learning)"]
    met = [("demora_prom_s", "Demora promedio (s)"),
           ("velocidad_kmh", "Velocidad (km/h)"),
           ("pct_detenido", "% tiempo detenido"),
           ("paradas_por_veh", "Paradas por vehiculo")]
    fig, axes = plt.subplots(2, 2, figsize=(11, 7))
    colores = ["#95A5A6", VERDE, AZUL]
    for ax, (k, lab) in zip(axes.flat, met):
        vals = [res[e][k] for e in escs]
        bars = ax.bar(etiquetas, vals, color=colores)
        ax.set_title(lab, fontsize=11)
        ax.grid(axis="y", alpha=0.3)
        ax.tick_params(axis="x", labelsize=8)
        for b, v in zip(bars, vals):
            ax.text(b.get_x() + b.get_width() / 2, v, f"{v}", ha="center",
                    va="bottom", fontsize=9, fontweight="bold")
    fig.suptitle("Comparacion de estrategias de control (demanda nominal)", fontweight="bold")
    fig.tight_layout()
    fig.savefig(fname, dpi=120)
    plt.close(fig)


def colas_tiempo(models, fname):
    """Longitud de cola en el corredor por cruce a lo largo del tiempo."""
    fig, axes = plt.subplots(1, 3, figsize=(13, 4), sharey=True)
    nombres = {"sin_coord": "Sin coordinacion", "onda_verde": "Onda verde",
               "adaptativo": "Adaptativo"}
    col = {"sin_coord": "#95A5A6", "onda_verde": VERDE, "adaptativo": AZUL}
    for ax, cid in zip(axes, C.ORDER):
        for modo, m in models.items():
            q = m.qcor_hist[cid]
            tt = np.arange(len(q)) * m.dt
            ax.plot(tt, q, label=nombres[modo], color=col[modo], lw=1.4)
        ax.set_title(f"{cid} - {C.NOMBRES[cid]}")
        ax.set_xlabel("tiempo (s)")
        ax.grid(True, alpha=0.3)
    axes[0].set_ylabel("cola en el corredor (veh)")
    axes[0].legend(fontsize=8)
    fig.suptitle("Longitud de cola por cruce", fontweight="bold")
    fig.tight_layout()
    fig.savefig(fname, dpi=120)
    plt.close(fig)


def convergencia_qlearning(hist, fname):
    """Curva de aprendizaje del control adaptativo."""
    r = np.array(hist["reward"])
    w = 25
    sm = np.convolve(r, np.ones(w) / w, "valid")
    fig, ax = plt.subplots(figsize=(9, 4.5))
    ax.plot(r, color="#bdc3c7", lw=0.8, label="recompensa por episodio")
    ax.plot(np.arange(len(sm)) + w // 2, sm, color=AZUL, lw=2.2,
            label=f"media movil ({w})")
    ax.set_xlabel("episodio de entrenamiento")
    ax.set_ylabel("recompensa  (- colas)")
    ax.set_title("Convergencia del control adaptativo (Q-learning)")
    ax.legend()
    ax.grid(True, alpha=0.3)
    fig.tight_layout()
    fig.savefig(fname, dpi=120)
    plt.close(fig)


def sensibilidad_velocidad(fname, v_diseno=50, vmin=30, vmax=70):
    """
    Sensibilidad de la onda verde a la velocidad REAL de circulacion.

    Los offsets se fijan al diseno (v_diseno). Luego se varia la velocidad real
    del peloton: la coordinacion es maxima a la velocidad de diseno y se degrada
    al alejarse (el peloton llega antes o despues del verde). Responde a la
    pregunta: si el trafico no circula a la velocidad de sincronia, ¿se rompe la
    onda verde?
    """
    from .control import offsets_onda_verde
    offs = offsets_onda_verde(v_diseno / 3.6)
    d = [C.DIST[c] for c in C.ORDER]
    vels = np.arange(vmin, vmax + 1, 2)
    coord = []
    headway = 1.5
    platoon = 40
    for vk in vels:
        va = vk / 3.6
        verdes = 0
        for car in range(platoon):
            t0 = car * headway
            for i, cid in enumerate(C.ORDER):
                arr = t0 + d[i] / va
                local = (arr - offs[cid]) % C.CYCLE
                if 0 <= local < C.GREEN:
                    verdes += 1
        coord.append(100.0 * verdes / (platoon * len(C.ORDER)))
    fig, ax = plt.subplots(figsize=(9, 4.5))
    ax.plot(vels, coord, "o-", color=VERDE, lw=2)
    ax.axvline(v_diseno, color=NARANJA, ls="--",
               label=f"velocidad de diseno ({v_diseno} km/h)")
    ax.set_xlabel("velocidad real de circulacion (km/h)")
    ax.set_ylabel("indice de coordinacion (% llegadas en verde)")
    ax.set_title("Sensibilidad de la onda verde a la velocidad real")
    ax.legend()
    ax.grid(True, alpha=0.3)
    ax.set_ylim(0, 100)
    fig.tight_layout()
    fig.savefig(fname, dpi=120)
    plt.close(fig)
