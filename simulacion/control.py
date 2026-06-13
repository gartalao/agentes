"""
Estrategias de control y coordinacion semaforica.

1) Offsets de onda verde (heuristica de progresion): offset_i = (offset_{i-1} +
   d_i / v) mod C. Es el calculo clasico de la onda verde.

2) Q-learning (control adaptativo): cada cruce es un agente que aprende, por
   refuerzo, que reparto de verde dar segun las colas SENSADAS en sus accesos.
   Estado = (cola_corredor, cola_transversal) discretizadas; accion = fraccion
   de verde para el corredor; recompensa = - (colas resultantes). Formulacion
   identica a la vista en clase (implementacion_MDP_QLearning):
       Q[s][a] <- Q[s][a] + alpha * (r + gamma * max_a' Q[s'][a'] - Q[s][a])

La politica aprendida NO son tiempos fijos: es una funcion estado -> accion que
el controlador evalua en vivo con lo que miden sus detectores (camara/sensor),
por lo que es desplegable en la vida real.
"""
import numpy as np

from . import corredor as C

# acciones del controlador adaptativo: VERDE del corredor en segundos.
# Nunca baja del verde base coordinado (C.GREEN): solo lo MANTIENE o lo EXTIENDE,
# de modo que la banda de la onda verde nunca se rompe (solo se alarga).
GREEN_OPTIONS = [46.0, 54.0, 62.0, 70.0]

# flujos de saturacion para el modelo de colas de entrenamiento (veh/s),
# consistentes con la microsimulacion de un carril (~1 veh / 2 s de headway)
SAT_COR = 0.5
SAT_TR = 0.5


# ---------------------------------------------------------------- offsets
def offsets_onda_verde(v=C.V_SYNC, cycle=C.CYCLE):
    """offset_i = (offset_{i-1} + d_i / v) mod C (progresion hacia adelante)."""
    d = [C.DIST[c] for c in C.ORDER]
    off = [0.0]
    for i in range(1, len(d)):
        off.append((off[-1] + (d[i] - d[i - 1]) / v) % cycle)
    return {c: round(o, 2) for c, o in zip(C.ORDER, off)}


def offsets_por_escenario(escenario, v=C.V_SYNC):
    if escenario == "sin_coord":
        return {c: 0.0 for c in C.ORDER}
    # onda_verde y adaptativo comparten el ancla de coordinacion (offset d/v)
    return offsets_onda_verde(v)


# ------------------------------------------------------ Q-learning (adaptativo)
def _bucket(x):
    if x <= 0:
        return 0
    if x <= 3:
        return 1
    if x <= 8:
        return 2
    return 3


def _usable():
    return C.CYCLE - 2.0 * (C.YELLOW + C.ALLRED)


def entrenar_qlearning(lambda_in=0.28, lambda_tr=None,
                       episodios=500, pasos=80, seed=7,
                       alpha=0.15, gamma=0.90, epsilon=0.40, decay=0.99):
    """
    Entrena una tabla Q por cruce sobre un modelo de colas por ciclo (rapido y
    fiel al sensado del microsim), a una demanda representativa de hora pico.
    Devuelve (policy, ser, hist).

    policy[cid][(b_cor, b_tr)] = vector Q sobre GREEN_OPTIONS.
    Update de Q-learning identico al visto en clase:
        Q[s][a] <- Q[s][a] + alpha * (r + gamma * max_a' Q[s'][a'] - Q[s][a])
    """
    if lambda_tr is None:
        lambda_tr = {"C3": 0.12, "C2": 0.18, "C1": 0.14}
    rng = np.random.default_rng(seed)
    estados = [(a, b) for a in range(4) for b in range(4)]
    nA = len(GREEN_OPTIONS)
    policy = {c: {s: np.zeros(nA) for s in estados} for c in C.ORDER}
    hist = {"reward": [], "cola": []}
    eps = epsilon

    for ep in range(episodios):
        q_cor = {c: 0.0 for c in C.ORDER}
        q_tr = {c: 0.0 for c in C.ORDER}
        ep_reward = 0.0
        ep_cola = 0.0
        for _ in range(pasos):
            for cid in C.ORDER:
                s = (_bucket(q_cor[cid]), _bucket(q_tr[cid]))
                if rng.random() < eps:
                    a = rng.integers(nA)
                else:
                    a = int(np.argmax(policy[cid][s]))
                g_cor = GREEN_OPTIONS[a]
                g_tr = max(6.0, _usable() - g_cor)
                # llegadas en el ciclo
                arr_cor = rng.poisson(lambda_in * C.CYCLE)
                arr_tr = rng.poisson(lambda_tr[cid] * C.CYCLE)
                # descarga
                q_cor[cid] = max(0.0, q_cor[cid] + arr_cor - SAT_COR * g_cor)
                q_tr[cid] = max(0.0, q_tr[cid] + arr_tr - SAT_TR * g_tr)
                r = -(q_cor[cid] + q_tr[cid])
                s2 = (_bucket(q_cor[cid]), _bucket(q_tr[cid]))
                best_next = float(np.max(policy[cid][s2]))
                policy[cid][s][a] += alpha * (r + gamma * best_next - policy[cid][s][a])
                ep_reward += r
                ep_cola += q_cor[cid] + q_tr[cid]
        hist["reward"].append(ep_reward / (pasos * len(C.ORDER)))
        hist["cola"].append(ep_cola / (pasos * len(C.ORDER)))
        eps = max(0.05, eps * decay)

    # convertir a listas serializables
    policy_ser = {c: {f"{s[0]},{s[1]}": policy[c][s].tolist() for s in estados}
                  for c in C.ORDER}
    return policy, policy_ser, hist


def politica_a_microsim(policy):
    """Adapta la policy entrenada (claves tupla) al formato que usa el microsim."""
    return policy


# ----------------------------------------- Q-learning que APRENDE los offsets
# (validacion: replica el ejemplo del curso Personalizacion_cruce_ondaverde_
# aprendizaje, donde la accion ES la configuracion de offsets del corredor)
def entrenar_offsets_qlearning(platoon=40, headway=1.5, green=C.GREEN,
                               episodios=800, seed=7,
                               alpha=0.15, gamma=0.90, epsilon=0.30, decay=0.995):
    rng = np.random.default_rng(seed)
    opciones = [0, 10, 20, 30, 40, 50, 60, 70, 80]
    acciones = [(0, o2, o3) for o2 in opciones for o3 in opciones]
    Q = {a: 0.0 for a in acciones}
    tv = C.tiempos_viaje()
    t_arr = [0.0, tv[0], tv[0] + tv[1]]  # tiempos de viaje acumulados

    def evaluar(offs):
        exitos, paradas, verdes = 0, 0, 0
        for car in range(platoon):
            t0 = car * headway
            ok = True
            for i in range(len(C.ORDER)):
                ta = t0 + t_arr[i]
                local = (ta - offs[i]) % C.CYCLE
                if 0 <= local < green:
                    verdes += 1
                else:
                    paradas += 1
                    ok = False
            if ok:
                exitos += 1
        coord = verdes / (platoon * len(C.ORDER))
        return exitos, paradas, coord

    eps = epsilon
    hist = {"reward": [], "coord": []}
    for ep in range(episodios):
        if rng.random() < eps:
            a = acciones[rng.integers(len(acciones))]
        else:
            mx = max(Q.values())
            a = acciones[[i for i, ac in enumerate(acciones) if Q[ac] == mx][0]]
        exitos, paradas, coord = evaluar(a)
        reward = 10 * exitos - 2 * paradas + 50 * coord
        Q[a] += alpha * (reward - Q[a])
        hist["reward"].append(reward)
        hist["coord"].append(coord)
        eps = max(0.05, eps * decay)
    best = max(Q, key=Q.get)
    return best, hist, evaluar
