"""
Parametros del corredor Av. Gomez Morin (San Pedro Garza Garcia, N.L.).

Las distancias entre cruces se midieron sobre mapa del corredor real. El ciclo
y el reparto de fases corresponden al plan semaforico tipico de la avenida
(ciclo de 90 s, comun a los tres cruces para poder coordinarlos).

Vocabulario del curso: el corredor es el ENTORNO; los vehiculos y los semaforos
son los AGENTES; el reloj comun y los offsets son el mecanismo de COORDINACION.
"""

# --- velocidad de sincronia (justificada en el reporte) -------------------
# 50 km/h es el limite operativo del corredor y la velocidad a la que se
# disena la progresion (onda verde). Se convierte a m/s para la dinamica.
V_KMH = 50.0
V_SYNC = V_KMH / 3.6  # 13.89 m/s

# --- plan semaforico comun ------------------------------------------------
CYCLE = 90.0      # ciclo comun (s)
GREEN = 46.0      # verde para el corredor Gomez Morin (s)
YELLOW = 3.0      # amarillo (s)
ALLRED = 2.5      # todo-rojo de despeje (s)
SPLIT = GREEN / CYCLE  # reparto de verde del corredor (~0.51)

# --- geometria del corredor (3 cruces reales) -----------------------------
# Distancia acumulada sobre el corredor, medida sobre mapa, sentido C3 -> C1.
ORDER = ["C3", "C2", "C1"]
NOMBRES = {"C3": "Alfonso Reyes", "C2": "Magnolia", "C1": "Av. del Roble"}
DIST = {"C3": 0.0, "C2": 725.0, "C1": 1170.0}   # m sobre el corredor

# tramos entre cruces (m) y tiempos de viaje a velocidad de sincronia (s)
def tramos():
    d = [DIST[c] for c in ORDER]
    return [d[i] - d[i - 1] for i in range(1, len(d))]

def tiempos_viaje(v=V_SYNC):
    return [t / v for t in tramos()]

# --- demanda vehicular ----------------------------------------------------
# Calibrada al pico de Gomez Morin (~12,000-15,000 veh/h en el corredor segun
# el Gobierno de Monterrey). Para el sentido principal modelado tomamos una
# tasa de llegada tipo Poisson. La demanda transversal se representa con el
# reparto de verde (el corredor recibe SPLIT del ciclo).
DEMANDA = {
    "base": 0.20,   # veh/s por carril del corredor (operacion tipica)
    "alta": 0.32,   # escenario de mayor demanda (sensibilidad / hora pico)
}

# punto de entrada y salida sobre el eje del corredor (m)
S_ENTRADA = -60.0
S_SALIDA = DIST["C1"] + 80.0


def posiciones_senales():
    """Posiciones de los semaforos sobre el eje del corredor (m)."""
    return [DIST[c] for c in ORDER]
