"""
Microsimulacion del corredor con AgentPy.

Agentes:
  - VehiculoAgente: auto que circula por el corredor con el modelo de
    seguimiento IDM (Intelligent Driver Model). Percibe a su lider y el
    estado de SU semaforo; jamas cruza en rojo.
  - SemaforoAgente: controlador de un cruce. SENSA las colas de sus accesos
    (corredor y transversal) mediante detectores (camara/sensor) y, segun el
    modo de control, decide el reparto de verde. Comparte reloj comun y offset
    con los demas (coordinacion de corredor).

Modos de control (ESTRATEGIAS comparadas):
  - 'sin_coord' : tiempo fijo, sin offset (cada semaforo aislado).
  - 'onda_verde': tiempo fijo COORDINADO por offsets = distancia / velocidad.
  - 'adaptativo': lazo cerrado; el controlador sensa colas y decide el reparto
    de verde con una politica aprendida por Q-learning (entrenada aparte).

El control adaptativo NO usa tiempos preprogramados: en cada ciclo lee los
detectores y elige la accion -> es desplegable en la vida real.
"""
import numpy as np
import agentpy as ap

from . import corredor as C


# ---- parametros IDM del agente vehiculo (mismos que el simulador Unity) ----
IDM_A = 1.7      # aceleracion maxima (m/s^2)
IDM_B = 2.6      # frenado comodo (m/s^2)
IDM_T = 1.35     # tiempo de seguimiento (s)
IDM_S0 = 3.2     # distancia minima en cola (m)
IDM_DELTA = 4.0
VEH_LEN = 4.6    # largo del vehiculo (m)

DET_ZONE = 100.0  # alcance del detector aguas arriba del semaforo (m)
V_STOP = 0.5     # umbral de "detenido" (m/s)
SAT_FLOW = 0.5   # flujo de saturacion de descarga transversal (veh/s)


def usable():
    """Verde repartible del ciclo, descontando amarillos y todo-rojo (s)."""
    return C.CYCLE - 2.0 * (C.YELLOW + C.ALLRED)


class VehiculoAgente(ap.Agent):
    def setup(self):
        self.s = C.S_ENTRADA           # posicion sobre el corredor (m)
        self.v = C.V_SYNC * 0.7        # velocidad inicial (m/s)
        self.v0 = C.V_SYNC             # velocidad deseada (m/s)
        self.estado = "circulando"
        self.t_spawn = self.model.t_now
        self.detenido_steps = 0
        self.paradas = 0
        self._moving_prev = True
        self.salio = False
        self.paradas_en = set()   # cruces donde este vehiculo se detuvo

    def accel(self, gap, dv):
        """Aceleracion IDM dado el hueco al lider y la velocidad relativa."""
        gap = max(0.5, gap)
        sstar = IDM_S0 + max(0.0, self.v * IDM_T + self.v * dv / (2.0 * np.sqrt(IDM_A * IDM_B)))
        return IDM_A * (1.0 - (self.v / max(0.1, self.v0)) ** IDM_DELTA - (sstar / gap) ** 2)


class SemaforoAgente(ap.Agent):
    def setup(self):
        self.cid = None            # se asigna al crear
        self.pos = 0.0             # posicion sobre el corredor (m)
        self.offset = 0.0          # desfase respecto al reloj comun (s)
        self.g_cor = C.GREEN       # verde del corredor este ciclo (s)
        self.q_cor = 0             # cola sensada en el corredor (detector)
        self.q_tr = 0.0            # cola en la transversal (detector)
        self.lambda_tr = 0.0       # demanda transversal (veh/s)
        # acumuladores para metricas/recompensa por ciclo
        self.q_cor_acc = 0.0
        self.q_tr_acc = 0.0
        self.cycle_samples = 0

    def fase_corredor(self, t):
        """Estado del semaforo para el corredor: 'verde' / 'amarillo' / 'rojo'.

        El verde del corredor SIEMPRE arranca en el tiempo local 0 (= offset):
        asi, extender el verde solo ALARGA la banda sin moverla, preservando la
        onda verde mientras se atiende mas demanda."""
        local = (t - self.offset) % C.CYCLE
        if local < self.g_cor:
            return "verde"
        if local < self.g_cor + C.YELLOW:
            return "amarillo"
        return "rojo"

    def transversal_en_verde(self, t):
        local = (t - self.offset) % C.CYCLE
        return (self.g_cor + C.YELLOW) <= local < (C.CYCLE - C.YELLOW)


class CorredorModel(ap.Model):
    """Entorno: el corredor Gomez Morin con sus 3 cruces."""

    def setup(self):
        self.dt = 0.5
        self.t_now = 0.0
        self.modo = self.p.get("modo", "onda_verde")
        self.lambda_in = self.p.get("lambda_in", C.DEMANDA["base"])
        self.policy = self.p.get("policy", None)   # tabla Q por cruce (adaptativo)
        self.semaforos = ap.AgentList(self, len(C.ORDER), SemaforoAgente)
        offs = self.p.get("offsets", {c: 0.0 for c in C.ORDER})
        lam_tr = self.p.get("lambda_tr", {c: 0.25 for c in C.ORDER})
        for sem, cid in zip(self.semaforos, C.ORDER):
            sem.cid = cid
            sem.pos = C.DIST[cid]
            sem.offset = offs[cid]
            sem.lambda_tr = lam_tr[cid]
            sem.g_cor = C.GREEN
        self.vehiculos = ap.AgentList(self, 0, VehiculoAgente)
        self._spawn_acc = 0.0
        self.rng = np.random.default_rng(self.p.get("seed", 7))
        # registros
        self.tray = []          # (id, t, s) trayectorias para el diagrama
        self.delays = []        # demora por vehiculo al salir
        self.paradas_veh = []   # paradas por vehiculo al salir
        self.veh_detenidos = 0  # muestras detenido
        self.veh_muestras = 0   # muestras totales
        self.cruces_verde = 0   # (veh,sem) cruzados en verde
        self.cruces_total = 0
        self.salidas = 0
        self.qcor_hist = {c: [] for c in C.ORDER}
        self.qtr_hist = {c: [] for c in C.ORDER}
        self.gcor_hist = {c: [] for c in C.ORDER}  # (t_inicio_ciclo, verde_corredor)
        self._next_id = 0

    # ---------- sensado (detectores tipo camara/sensor) ----------
    def sensar(self):
        for sem in self.semaforos:
            q = 0
            for v in self.vehiculos:
                if v.salio:
                    continue
                d = sem.pos - v.s
                if 0.0 <= d <= DET_ZONE and v.v < V_STOP:
                    q += 1
            sem.q_cor = q
            sem.q_cor_acc += q
            sem.q_tr_acc += sem.q_tr
            sem.cycle_samples += 1

    # ---------- control adaptativo: decide al inicio de cada ciclo ----------
    def _bucket(self, x):
        if x <= 0:
            return 0
        if x <= 3:
            return 1
        if x <= 8:
            return 2
        return 3

    def decidir_adaptativo(self):
        """Cada cruce sensa (q_cor,q_tr) y elige su verde de corredor (politica)."""
        from .control import GREEN_OPTIONS
        for sem in self.semaforos:
            s = (self._bucket(sem.q_cor), self._bucket(sem.q_tr))
            qrow = self.policy.get(sem.cid, {}).get(s)
            ai = 0 if qrow is None else int(np.argmax(qrow))
            sem.g_cor = GREEN_OPTIONS[ai]

    # ---------- dinamica ----------
    def step(self):
        t = self.t_now
        # 1) inicio de ciclo: sensar y (si adaptativo) decidir
        if abs(t % C.CYCLE) < self.dt:
            self.sensar()
            if self.modo == "adaptativo" and self.policy is not None:
                self.decidir_adaptativo()
            for sem in self.semaforos:
                sem.gcor_hist_local = sem.g_cor
                self.gcor_hist[sem.cid].append((t, sem.g_cor))

        # 2) llegadas Poisson al acceso del corredor
        self._spawn_acc += self.lambda_in * self.dt
        while self._spawn_acc >= 1.0:
            self._spawn_acc -= 1.0
            v = VehiculoAgente(self)
            v.id_num = self._next_id
            self._next_id += 1
            self.vehiculos.append(v)

        # 3) transversal: llegadas y descarga (cola por cruce)
        for sem in self.semaforos:
            sem.q_tr += self.rng.poisson(sem.lambda_tr * self.dt)
            if sem.transversal_en_verde(t):
                sem.q_tr = max(0.0, sem.q_tr - SAT_FLOW * self.dt)
            self.qcor_hist[sem.cid].append(sem.q_cor)
            self.qtr_hist[sem.cid].append(sem.q_tr)

        # 4) vehiculos del corredor: IDM + respeto de semaforo (nunca en rojo)
        vivos = [v for v in self.vehiculos if not v.salio]
        vivos.sort(key=lambda x: x.s, reverse=True)  # lider primero
        for i, v in enumerate(vivos):
            lead = vivos[i - 1] if i > 0 else None
            # restriccion del lider
            if lead is not None:
                gap = lead.s - v.s - VEH_LEN
                a = v.accel(gap, v.v - lead.v)
            else:
                a = v.accel(1e4, 0.0)
            # restriccion del semaforo: parar en rojo/amarillo-no-libra
            sem = self._sem_siguiente(v)
            if sem is not None:
                fase = sem.fase_corredor(t)
                dist = sem.pos - v.s
                if fase == "rojo":
                    a = min(a, v.accel(max(0.4, dist - IDM_S0), 0.0))
                elif fase == "amarillo":
                    # solo libra si alcanza a cruzar antes del rojo; si no, frena
                    if v.v * v.v / (2.0 * IDM_B) <= dist - 1.0:
                        a = min(a, v.accel(max(0.4, dist - IDM_S0), 0.0))
            a = max(-8.0, min(a, IDM_A))
            v.v = max(0.0, v.v + a * self.dt)
            # nunca rebasar la linea de alto en rojo (garantia dura)
            sem = self._sem_siguiente(v)
            adv = v.v * self.dt
            if sem is not None and sem.fase_corredor(t) != "verde":
                limite = sem.pos - 0.5
                if v.s <= limite and v.s + adv > limite:
                    adv = max(0.0, limite - v.s)
                    v.v = 0.0
            v.s += adv
            # metricas
            self.veh_muestras += 1
            moving = v.v >= V_STOP
            if not moving:
                self.veh_detenidos += 1
                # detector: registra en que cruce se detuvo (para coordinacion)
                sd = self._sem_siguiente(v)
                if sd is not None and 0.0 <= sd.pos - v.s <= DET_ZONE:
                    v.paradas_en.add(sd.cid)
            if self._moving_to_stopped(v, moving):
                v.paradas += 1
            v._moving_prev = moving
            self.tray.append((v.id_num, t, v.s, v.v))
            if v.s >= C.S_SALIDA:
                self._finish(v)

    def _moving_to_stopped(self, v, moving):
        return (v._moving_prev and not moving)

    def _sem_siguiente(self, v):
        best = None
        bd = 1e9
        for sem in self.semaforos:
            d = sem.pos - v.s
            if -1.0 < d < bd:
                bd = d
                best = sem
        return best

    def _finish(self, v):
        v.salio = True
        self.salidas += 1
        dist = C.S_SALIDA - C.S_ENTRADA
        free_t = dist / v.v0
        delay = max(0.0, (self.t_now - v.t_spawn) - free_t)
        self.delays.append(delay)
        self.paradas_veh.append(v.paradas)
        # cruces atravesados en verde (sin detenerse) de los 3
        verdes = len(C.ORDER) - len(v.paradas_en)
        self.cruces_verde += verdes
        self.cruces_total += len(C.ORDER)

    def update(self):
        self.t_now += self.dt

    def end(self):
        pass
