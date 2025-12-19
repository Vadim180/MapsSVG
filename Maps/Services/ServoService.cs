using System.IO.Ports;

namespace Maps.Services;

public class ServoController
{
    private SerialPort _serialPort;
    private int _currentAngle = 90; // Початковий кут
    private int _lastSentAngle;

    public ServoController()
    {
        string portName = FindArduinoPort();
        if (portName == null)
        {
            MessageBox.Show("Arduino не знайдено!", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            _serialPort = new SerialPort(portName, 9600);
            _serialPort.Open();
            SendAngle(_currentAngle);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Помилка підключення: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string FindArduinoPort()
    {
        return SerialPort.GetPortNames().FirstOrDefault();
    }

    public void MoveRight()
    {
        if (_serialPort != null && _serialPort.IsOpen && _currentAngle < 180)
        {
            _currentAngle++;
            SendAngle(_currentAngle);
        }
    }



    public void MoveLeft()
    {
        if (_serialPort != null && _serialPort.IsOpen && _currentAngle > 0)
        {
            _currentAngle--;
            SendAngle(_currentAngle);
        }
    }

    private void SendAngle(int angle)
    {
        if (_serialPort != null && _serialPort.IsOpen && angle != _lastSentAngle)
        {
            try
            {
                _serialPort.WriteLine(angle.ToString());
                _lastSentAngle = angle;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка надсилання даних: " + ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public void Close()
    {
        if (_serialPort != null)
        {
            _serialPort.Close();
            _serialPort.Dispose();
        }
    }
}
