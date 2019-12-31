import serial
import modbus_tk
import modbus_tk.defines as cst
from modbus_tk import modbus_rtu
import time,datetime
import sys
import pymssql

PORT = "/dev/ttyUSB0"
MPI_ID="FET01"
def connectDB():
    global conn
    conn = pymssql.connect(
        server = '192.168.240.120',
        user = 'sa',
        password = 'password',
        database = 'FAB'
    )
    
def main():
    logger = modbus_tk.utils.create_logger("console")
    try:
        #connect SQL Srv
        connectDB()
        #Connect to the slave
        master = modbus_rtu.RtuMaster(
            serial.Serial(port=PORT, baudrate=9600, bytesize=8, parity='N', stopbits=1, xonxoff=0)
        )
        master.set_timeout(1)
        master.set_verbose(True)
        logger.info("connected")
        while True:     
            time.sleep(5)            
            phValue=master.execute(99, cst.READ_INPUT_REGISTERS, 0, 1)
            logger.info('PH value：'+ str(phValue[0]/100))
            cur = conn.cursor()
            logger.info('SQL '+str(cur.callproc('insert_plc_recipe', (MPI_ID,'P0000',str(phValue[0]/100),datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")))))
            conn.commit()
                   
    except modbus_tk.modbus.ModbusError as exc:
        logger.error("%s- Code=%d", exc, exc.get_exception_code())
if __name__ == "__main__":
    main()
