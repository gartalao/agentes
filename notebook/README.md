# Notebook de onda verde

`onda_verde_gomez_morin.ipynb` modela el corredor Gómez Morín (C3 Alfonso
Reyes → C2 Magnolia → C1 Av. del Roble) como un sistema multiagente y lo
simula con AgentPy, comparando tres estrategias de control.

## Cómo correrlo

**Google Colab** (recomendado): subir el archivo y Runtime → Run all.
La primera celda instala `agentpy`. El notebook es autónomo: trae embebidas
las distancias reales del corredor y todo el código, así que corre sin
archivos extra.

**Local**: `pip install agentpy numpy matplotlib pandas` y abrirlo con
Jupyter.

## Qué contiene

1. **Parámetros del corredor**: distancias medidas, ciclo (90 s), velocidad
   de sincronía (50 km/h) y demanda calibrada al pico de Gómez Morín.
2. **Agentes**: vehículo con seguimiento vehicular IDM (nunca cruza en rojo)
   y semáforo que sensa las colas de sus accesos y fija su verde.
3. **Coordinación**: offsets de onda verde con
   `offset_i = (offset_{i-1} + d/v) mod C` y cálculo del ancho de banda.
4. **Control adaptativo (Q-learning)**: estado = colas discretizadas,
   acción = verde del corredor, recompensa = − colas; se grafica la
   convergencia y la política aprendida.
5. **Diagramas tiempo-espacio EMPÍRICOS**: bandas verde/rojo de cada
   semáforo con las trayectorias reales de los vehículos simulados, para los
   tres escenarios (sin coordinación, onda verde y adaptativo).
6. **Comparación de escenarios**: demora, velocidad, % detenido, paradas,
   throughput, colas por cruce y sensibilidad a la velocidad de sincronía.
7. **Exportación**: escribe `plan_control.json` (offsets y política
   aprendida) que Unity ejecuta sobre la maqueta 3D.
